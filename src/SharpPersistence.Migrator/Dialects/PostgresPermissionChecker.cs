using System.Data.Common;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Dialects;

internal static class PostgresPermissionChecker
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

        var (currentUser, currentDb, isSuperuser, hasDbCreate) = await ReadRoleInfoAsync(conn, ct).ConfigureAwait(false);
        if (isSuperuser)
        {
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Pass,
                $"Database permissions verified (PostgreSQL superuser '{currentUser}')"));
            return checks;
        }

        var schemasNeeded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in req.SchemasToCreate) schemasNeeded.Add(NormalizePgSchema(s));
        foreach (var s in req.SchemasTouched) schemasNeeded.Add(NormalizePgSchema(s));

        var schemaPrivs = await ReadSchemaPrivilegesAsync(conn, schemasNeeded, ct).ConfigureAwait(false);
        var createSchemasNeeded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in req.TableCreateSchemas) createSchemasNeeded.Add(NormalizePgSchema(s));
        foreach (var s in req.SequenceCreateSchemas) createSchemasNeeded.Add(NormalizePgSchema(s));
        foreach (var s in req.TypeSchemas) createSchemasNeeded.Add(NormalizePgSchema(s));

        var dbCreateReported = false;
        foreach (var schema in schemasNeeded.OrderBy(static s => s, StringComparer.OrdinalIgnoreCase))
        {
            if (!schemaPrivs.TryGetValue(schema, out var priv))
            {
                if (req.SchemasToCreate.Contains(schema))
                {
                    if (!hasDbCreate && !dbCreateReported)
                    {
                        dbCreateReported = true;
                        checks.Add(new DoctorCheckItem(
                            DoctorSeverity.Error,
                            $"Role '{currentUser}' lacks CREATE privilege on database '{currentDb}' (required to create schema '{schema}').",
                            Hint: $"GRANT CREATE ON DATABASE \"{currentDb}\" TO \"{currentUser}\";"));
                    }
                }
                else
                {
                    checks.Add(new DoctorCheckItem(
                        DoctorSeverity.Error,
                        $"Target schema '{schema}' does not exist in database '{currentDb}'.",
                        Hint: $"CREATE SCHEMA \"{schema}\" AUTHORIZATION \"{currentUser}\";"));
                }

                continue;
            }

            if (!priv.HasUsage)
            {
                checks.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    $"Role '{currentUser}' lacks USAGE privilege on schema '{schema}'.",
                    Hint: $"GRANT USAGE ON SCHEMA \"{schema}\" TO \"{currentUser}\";"));
            }

            if (createSchemasNeeded.Contains(schema) && !priv.HasCreate)
            {
                checks.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    $"Role '{currentUser}' lacks CREATE privilege on schema '{schema}'.",
                    Hint: $"GRANT CREATE ON SCHEMA \"{schema}\" TO \"{currentUser}\";"));
            }
        }

        var ownedTargets = new HashSet<(string Schema, string Table)>();
        foreach (var t in req.TableAlters) ownedTargets.Add((NormalizePgSchema(t.Schema), t.Name));
        foreach (var t in req.TableDrops) ownedTargets.Add((NormalizePgSchema(t.Schema), t.Name));
        foreach (var t in req.IndexCreateTables) ownedTargets.Add((NormalizePgSchema(t.Schema), t.Name));

        var refTargets = req.ForeignKeyReferences
            .Select(f => (Schema: NormalizePgSchema(f.PrincipalSchema), Table: f.PrincipalTable, f.ConstraintName))
            .ToList();

        var tableQuerySchemas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (s, _) in ownedTargets)
        {
            if (schemaPrivs.ContainsKey(s)) tableQuerySchemas.Add(s);
        }

        foreach (var (s, _, _) in refTargets)
        {
            if (schemaPrivs.ContainsKey(s)) tableQuerySchemas.Add(s);
        }

        if (tableQuerySchemas.Count > 0)
        {
            var tablePrivs = await ReadTablePrivilegesAsync(conn, tableQuerySchemas, ct).ConfigureAwait(false);

            foreach (var (schema, table) in ownedTargets.OrderBy(static x => x.Schema).ThenBy(static x => x.Table))
            {
                if (tablePrivs.TryGetValue((schema, table), out var info) && !info.IsOwner)
                {
                    checks.Add(new DoctorCheckItem(
                        DoctorSeverity.Error,
                        $"Role '{currentUser}' does not own existing table '{schema}.{table}' (required for ALTER/DROP TABLE or CREATE INDEX).",
                        Hint: $"ALTER TABLE \"{schema}\".\"{table}\" OWNER TO \"{currentUser}\";"));
                }
            }

            var reportedRefs = new HashSet<(string Schema, string Table)>();
            foreach (var (schema, table, constraintName) in refTargets)
            {
                if (tablePrivs.TryGetValue((schema, table), out var info) &&
                    !info.HasReferences &&
                    reportedRefs.Add((schema, table)))
                {
                    var fkLabel = string.IsNullOrWhiteSpace(constraintName) ? string.Empty : $" (required by '{constraintName}')";
                    checks.Add(new DoctorCheckItem(
                        DoctorSeverity.Error,
                        $"Role '{currentUser}' lacks REFERENCES privilege on '{schema}.{table}'{fkLabel}.",
                        Hint: $"GRANT REFERENCES ON TABLE \"{schema}\".\"{table}\" TO \"{currentUser}\";"));
                }
            }
        }

        if (req.Extensions.Count > 0)
        {
            await CheckExtensionsAsync(conn, currentUser, hasDbCreate, req.Extensions, checks, ct).ConfigureAwait(false);
        }

        if (req.Tablespaces.Count > 0)
        {
            await CheckTablespacesAsync(conn, currentUser, req.Tablespaces, checks, ct).ConfigureAwait(false);
        }

        if (checks.Count == 0)
        {
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Pass,
                $"Database permissions verified for PostgreSQL role '{currentUser}'"));
        }

        return checks;
    }

    private static async Task<(string User, string Db, bool IsSuperuser, bool HasDbCreate)> ReadRoleInfoAsync(
        DbConnection conn,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT current_user, current_database(), r.rolsuper,
                   has_database_privilege(current_user, current_database(), 'CREATE')
            FROM pg_catalog.pg_roles r
            WHERE r.rolname = current_user
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return (reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), reader.GetBoolean(3));
        }

        return ("current_user", conn.Database, false, false);
    }

    private static async Task<Dictionary<string, (bool HasUsage, bool HasCreate)>> ReadSchemaPrivilegesAsync(
        DbConnection conn,
        HashSet<string> schemas,
        CancellationToken ct)
    {
        var result = new Dictionary<string, (bool, bool)>(StringComparer.OrdinalIgnoreCase);
        if (schemas.Count == 0) return result;

        await using var cmd = conn.CreateCommand();
        var placeholders = AddInParameters(cmd, "s", schemas);
        cmd.CommandText = $"""
            SELECT n.nspname,
                   has_schema_privilege(current_user, n.oid, 'USAGE'),
                   has_schema_privilege(current_user, n.oid, 'CREATE')
            FROM pg_catalog.pg_namespace n
            WHERE n.nspname IN ({placeholders})
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result[reader.GetString(0)] = (reader.GetBoolean(1), reader.GetBoolean(2));
        }

        return result;
    }

    private static async Task<Dictionary<(string Schema, string Table), (bool IsOwner, bool HasReferences)>> ReadTablePrivilegesAsync(
        DbConnection conn,
        HashSet<string> schemas,
        CancellationToken ct)
    {
        var result = new Dictionary<(string, string), (bool, bool)>();
        await using var cmd = conn.CreateCommand();
        var placeholders = AddInParameters(cmd, "ts", schemas);
        cmd.CommandText = $"""
            SELECT n.nspname, c.relname,
                   pg_catalog.pg_has_role(current_user, c.relowner, 'MEMBER'),
                   has_table_privilege(current_user, c.oid, 'REFERENCES')
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r', 'p') AND n.nspname IN ({placeholders})
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result[(reader.GetString(0), reader.GetString(1))] = (reader.GetBoolean(2), reader.GetBoolean(3));
        }

        return result;
    }

    private static async Task CheckExtensionsAsync(
        DbConnection conn,
        string currentUser,
        bool hasDbCreate,
        HashSet<string> extensions,
        List<DoctorCheckItem> checks,
        CancellationToken ct)
    {
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trusted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var cmd = conn.CreateCommand();
        var placeholders = AddInParameters(cmd, "ext", extensions);
        cmd.CommandText = $"""
            SELECT e.extname, TRUE AS is_installed, FALSE AS is_trusted
            FROM pg_catalog.pg_extension e
            WHERE e.extname IN ({placeholders})
            UNION ALL
            SELECT a.name, FALSE, COALESCE(bool_or(a.trusted), FALSE)
            FROM pg_catalog.pg_available_extension_versions a
            WHERE a.name IN ({placeholders})
            GROUP BY a.name
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            if (reader.GetBoolean(1)) installed.Add(name);
            if (reader.GetBoolean(2)) trusted.Add(name);
        }

        foreach (var ext in extensions.OrderBy(static e => e, StringComparer.OrdinalIgnoreCase))
        {
            if (installed.Contains(ext)) continue;
            if (trusted.Contains(ext) && hasDbCreate) continue;

            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Error,
                $"Role '{currentUser}' cannot install PostgreSQL extension '{ext}' (not installed and requires superuser or trusted extension + database CREATE).",
                Hint: $"Ask a superuser to run: CREATE EXTENSION IF NOT EXISTS \"{ext}\";"));
        }
    }

    private static async Task CheckTablespacesAsync(
        DbConnection conn,
        string currentUser,
        HashSet<string> tablespaces,
        List<DoctorCheckItem> checks,
        CancellationToken ct)
    {
        var found = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = conn.CreateCommand();
        var placeholders = AddInParameters(cmd, "sp", tablespaces);
        cmd.CommandText = $"""
            SELECT spcname, has_tablespace_privilege(current_user, oid, 'CREATE')
            FROM pg_catalog.pg_tablespace
            WHERE spcname IN ({placeholders})
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            found[reader.GetString(0)] = reader.GetBoolean(1);
        }

        foreach (var ts in tablespaces.OrderBy(static t => t, StringComparer.OrdinalIgnoreCase))
        {
            if (!found.TryGetValue(ts, out var hasCreate))
            {
                checks.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    $"Tablespace '{ts}' does not exist in PostgreSQL.",
                    Hint: $"Create tablespace '{ts}' or update UseTablespace(...) in your entity configuration."));
            }
            else if (!hasCreate)
            {
                checks.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    $"Role '{currentUser}' lacks CREATE privilege on tablespace '{ts}'.",
                    Hint: $"GRANT CREATE ON TABLESPACE \"{ts}\" TO \"{currentUser}\";"));
            }
        }
    }

    private static string AddInParameters(DbCommand cmd, string prefix, IEnumerable<string> values)
    {
        var names = new List<string>();
        var idx = 0;
        foreach (var v in values)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = $"@{prefix}{idx}";
            p.Value = v;
            cmd.Parameters.Add(p);
            names.Add(p.ParameterName);
            idx++;
        }

        return string.Join(", ", names);
    }

    private static string NormalizePgSchema(string? schema) =>
        string.IsNullOrWhiteSpace(schema) ? "public" : schema.Trim();
}
