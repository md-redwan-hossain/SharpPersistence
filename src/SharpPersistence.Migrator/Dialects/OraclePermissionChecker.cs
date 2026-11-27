using System.Data.Common;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Dialects;

internal static class OraclePermissionChecker
{
    public static async Task<IReadOnlyList<DoctorCheckItem>> CheckAsync(
        DbConnection conn,
        DatabasePermissionRequirements req,
        CancellationToken ct)
    {
        var checks = new List<DoctorCheckItem>();
        if (!req.HasAnyRequirements)
        {
            checks.Add(new DoctorCheckItem(DoctorSeverity.Pass, "No DDL permission requirements to verify"));
            return checks;
        }

        var (currentUser, defaultTablespace) = await ReadUserInfoAsync(conn, ct).ConfigureAwait(false);
        var privs = await ReadSessionPrivilegesAsync(conn, ct).ConfigureAwait(false);

        string ResolveSchema(string? raw) =>
            string.IsNullOrWhiteSpace(raw) ? currentUser : raw.Trim().ToUpperInvariant();

        var otherSchemas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in req.SchemasTouched)
        {
            var resolved = ResolveSchema(s);
            if (!string.Equals(resolved, currentUser, StringComparison.OrdinalIgnoreCase))
            {
                otherSchemas.Add(resolved);
            }
        }

        foreach (var s in req.SchemasToCreate)
        {
            var resolved = ResolveSchema(s);
            if (!string.Equals(resolved, currentUser, StringComparison.OrdinalIgnoreCase))
            {
                otherSchemas.Add(resolved);
            }
        }

        var existingOtherSchemas = await ReadExistingUsersAsync(conn, otherSchemas, ct).ConfigureAwait(false);
        foreach (var schema in otherSchemas.OrderBy(static s => s, StringComparer.OrdinalIgnoreCase))
        {
            if (!existingOtherSchemas.Contains(schema))
            {
                checks.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    $"Schema '{schema}' does not exist in Oracle (current user: '{currentUser}').",
                    Hint: $"Ask a DBA to create user/schema '{schema}' (CREATE USER \"{schema}\" ...)."));
            }
        }

        CheckSystemPrivileges(currentUser, privs, req, ResolveSchema, checks);

        if (!privs.Contains("UNLIMITED TABLESPACE") &&
            (req.TableCreateSchemas.Count > 0 || req.IndexCreateTables.Count > 0 || req.Tablespaces.Count > 0))
        {
            await CheckTablespaceQuotasAsync(conn, currentUser, defaultTablespace, req, ResolveSchema, checks, ct)
                .ConfigureAwait(false);
        }

        if (!privs.Contains("REFERENCES ANY TABLE") && req.ForeignKeyReferences.Count > 0)
        {
            await CheckForeignKeyReferencesAsync(conn, currentUser, existingOtherSchemas, req, ResolveSchema, checks, ct)
                .ConfigureAwait(false);
        }

        if (checks.Count == 0)
        {
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Pass,
                $"Database permissions verified for Oracle user '{currentUser}'"));
        }

        return checks;
    }

    private static void CheckSystemPrivileges(
        string currentUser,
        HashSet<string> privs,
        DatabasePermissionRequirements req,
        Func<string?, string> resolveSchema,
        List<DoctorCheckItem> checks)
    {
        bool IsOwn(string? s) => string.Equals(resolveSchema(s), currentUser, StringComparison.OrdinalIgnoreCase);

        void Require(string privilege, string reason)
        {
            if (!privs.Contains(privilege))
            {
                checks.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    $"Oracle user '{currentUser}' lacks {privilege} privilege ({reason}).",
                    Hint: $"GRANT {privilege} TO \"{currentUser}\";"));
            }
        }

        if (req.TableCreateSchemas.Any(IsOwn) && !privs.Contains("CREATE TABLE") && !privs.Contains("CREATE ANY TABLE"))
        {
            Require("CREATE TABLE", "required to create tables in own schema");
        }

        if (req.TableCreateSchemas.Any(s => !IsOwn(s)))
        {
            Require("CREATE ANY TABLE", "required to create tables in another schema");
        }

        if (req.TableAlters.Any(t => !IsOwn(t.Schema)))
        {
            Require("ALTER ANY TABLE", "required to alter tables in another schema");
        }

        if (req.TableDrops.Any(t => !IsOwn(t.Schema)))
        {
            Require("DROP ANY TABLE", "required to drop tables in another schema");
        }

        if (req.SequenceCreateSchemas.Any(IsOwn) && !privs.Contains("CREATE SEQUENCE") && !privs.Contains("CREATE ANY SEQUENCE"))
        {
            Require("CREATE SEQUENCE", "required to create sequences in own schema");
        }

        if (req.SequenceCreateSchemas.Any(s => !IsOwn(s)))
        {
            Require("CREATE ANY SEQUENCE", "required to create sequences in another schema");
        }

        if (req.SequenceAlterSchemas.Any(s => !IsOwn(s)))
        {
            Require("ALTER ANY SEQUENCE", "required to alter sequences in another schema");
        }

        if (req.SequenceDropSchemas.Any(s => !IsOwn(s)))
        {
            Require("DROP ANY SEQUENCE", "required to drop sequences in another schema");
        }

        if (req.IndexCreateTables.Any(t => !IsOwn(t.Schema)))
        {
            Require("CREATE ANY INDEX", "required to create indexes in another schema");
        }

        if (req.IndexDropSchemas.Any(s => !IsOwn(s)))
        {
            Require("DROP ANY INDEX", "required to drop indexes in another schema");
        }
    }

    private static async Task CheckTablespaceQuotasAsync(
        DbConnection conn,
        string currentUser,
        string? defaultTablespace,
        DatabasePermissionRequirements req,
        Func<string?, string> resolveSchema,
        List<DoctorCheckItem> checks,
        CancellationToken ct)
    {
        var quotas = new Dictionary<string, (long MaxBytes, long UsedBytes)>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT TABLESPACE_NAME, MAX_BYTES, BYTES FROM USER_TS_QUOTAS";
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                quotas[reader.GetString(0)] = (Convert.ToInt64(reader.GetValue(1)), Convert.ToInt64(reader.GetValue(2)));
            }
        }

        var requiredTs = new HashSet<string>(req.Tablespaces, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(defaultTablespace) &&
            (req.TableCreateSchemas.Any(s => string.Equals(resolveSchema(s), currentUser, StringComparison.OrdinalIgnoreCase)) ||
             req.IndexCreateTables.Any(t => string.Equals(resolveSchema(t.Schema), currentUser, StringComparison.OrdinalIgnoreCase))))
        {
            requiredTs.Add(defaultTablespace);
        }

        foreach (var ts in requiredTs.OrderBy(static t => t, StringComparer.OrdinalIgnoreCase))
        {
            if (!quotas.TryGetValue(ts, out var q) || (q.MaxBytes >= 0 && q.MaxBytes <= q.UsedBytes))
            {
                checks.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    $"Oracle user '{currentUser}' has no available quota on tablespace '{ts}' (will trigger ORA-01950).",
                    Hint: $"ALTER USER \"{currentUser}\" QUOTA UNLIMITED ON \"{ts}\";"));
            }
        }
    }

    private static async Task CheckForeignKeyReferencesAsync(
        DbConnection conn,
        string currentUser,
        HashSet<string> existingOtherSchemas,
        DatabasePermissionRequirements req,
        Func<string?, string> resolveSchema,
        List<DoctorCheckItem> checks,
        CancellationToken ct)
    {
        var crossRefs = req.ForeignKeyReferences
            .Select(f => (Schema: resolveSchema(f.PrincipalSchema), Table: f.PrincipalTable.ToUpperInvariant(), f.ConstraintName))
            .Where(x => !string.Equals(x.Schema, currentUser, StringComparison.OrdinalIgnoreCase) &&
                        existingOtherSchemas.Contains(x.Schema))
            .ToList();
        if (crossRefs.Count == 0) return;

        var targetSchemas = crossRefs.Select(x => x.Schema).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var existingTables = new HashSet<(string Owner, string Table)>();
        await using (var cmd = conn.CreateCommand())
        {
            OracleCommandHelper.EnableBindByName(cmd);
            var placeholders = AddInParameters(cmd, "o", targetSchemas);
            cmd.CommandText = $"SELECT OWNER, TABLE_NAME FROM ALL_TABLES WHERE UPPER(OWNER) IN ({placeholders})";
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                existingTables.Add((reader.GetString(0).ToUpperInvariant(), reader.GetString(1).ToUpperInvariant()));
            }
        }

        var grantedRefs = new HashSet<(string Owner, string Table)>();
        await using (var cmd = conn.CreateCommand())
        {
            OracleCommandHelper.EnableBindByName(cmd);
            var placeholders = AddInParameters(cmd, "g", targetSchemas);
            cmd.CommandText = $"""
                SELECT OWNER, TABLE_NAME
                FROM USER_TAB_PRIVS_RECD
                WHERE PRIVILEGE = 'REFERENCES' AND UPPER(OWNER) IN ({placeholders})
                """;
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                grantedRefs.Add((reader.GetString(0).ToUpperInvariant(), reader.GetString(1).ToUpperInvariant()));
            }
        }

        var reported = new HashSet<(string Owner, string Table)>();
        foreach (var (schema, table, constraintName) in crossRefs)
        {
            if (existingTables.Contains((schema, table)) &&
                !grantedRefs.Contains((schema, table)) &&
                reported.Add((schema, table)))
            {
                var fkLabel = string.IsNullOrWhiteSpace(constraintName) ? string.Empty : $" (required by '{constraintName}')";
                checks.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    $"Oracle user '{currentUser}' lacks direct REFERENCES privilege on '{schema}.{table}'{fkLabel}.",
                    Hint: $"GRANT REFERENCES ON \"{schema}\".\"{table}\" TO \"{currentUser}\";"));
            }
        }
    }

    private static async Task<(string User, string? DefaultTablespace)> ReadUserInfoAsync(DbConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT USERNAME, DEFAULT_TABLESPACE FROM USER_USERS";
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return (reader.GetString(0).ToUpperInvariant(), reader.IsDBNull(1) ? null : reader.GetString(1));
        }

        return ("USER", null);
    }

    private static async Task<HashSet<string>> ReadSessionPrivilegesAsync(DbConnection conn, CancellationToken ct)
    {
        var privs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT PRIVILEGE FROM SESSION_PRIVS";
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            privs.Add(reader.GetString(0));
        }

        return privs;
    }

    private static async Task<HashSet<string>> ReadExistingUsersAsync(
        DbConnection conn,
        HashSet<string> schemas,
        CancellationToken ct)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (schemas.Count == 0) return found;

        await using var cmd = conn.CreateCommand();
        OracleCommandHelper.EnableBindByName(cmd);
        var placeholders = AddInParameters(cmd, "u", schemas);
        cmd.CommandText = $"SELECT USERNAME FROM ALL_USERS WHERE UPPER(USERNAME) IN ({placeholders})";
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            found.Add(reader.GetString(0).ToUpperInvariant());
        }

        return found;
    }

    private static string AddInParameters(DbCommand cmd, string prefix, IEnumerable<string> values)
    {
        var names = new List<string>();
        var idx = 0;
        foreach (var v in values)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = $"{prefix}{idx}";
            p.Value = v.ToUpperInvariant();
            cmd.Parameters.Add(p);
            names.Add($":{p.ParameterName}");
            idx++;
        }

        return string.Join(", ", names);
    }
}
