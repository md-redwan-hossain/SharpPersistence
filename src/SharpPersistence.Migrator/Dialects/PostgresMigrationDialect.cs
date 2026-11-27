using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Models;
using static SharpPersistence.Migrator.Dialects.SqlFormatHelper;

namespace SharpPersistence.Migrator.Dialects;

internal sealed class PostgresMigrationDialect : IMigrationDialect
{
    private static DbProviderFactory? _cachedFactory;

    public SqlDialect Dialect => SqlDialect.PostgreSql;
    public string PingSql => "SELECT 1";
    public bool SupportsGlobalExtensionsAndEnums => true;
    public bool SupportsComputedStorageMode => true;
    public bool SupportsTransactionalDdl => true;
    public string ProviderPackageHint => "Install the Npgsql NuGet package or pass a DbProviderFactory / connection factory.";

    public ISqlDialectGenerator CreateDdlGenerator() => new PostgresDialectGenerator();

    public DbProviderFactory? ResolveDefaultProviderFactory() =>
        _cachedFactory ??= MigrationDialectRegistry.ResolveProviderFactory("Npgsql.NpgsqlFactory", "Npgsql");

    public string NormalizeSchemaName(string? schema) =>
        string.IsNullOrWhiteSpace(schema) ? "public" : schema.Trim();

    public bool AreSqlTypesCompatible(string depSqlType, string prinSqlType) =>
        string.Equals(depSqlType, prinSqlType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(NormalizeTypeName(depSqlType), NormalizeTypeName(prinSqlType), StringComparison.OrdinalIgnoreCase);

    public string ResolveSequenceSqlType(Type clrType)
    {
        var t = Nullable.GetUnderlyingType(clrType) ?? clrType;
        return t == typeof(short) ? "smallint" : t == typeof(int) ? "integer" : "bigint";
    }

    public IEnumerable<string> SplitScriptStatements(string sql)
    {
        yield return sql;
    }

    public string FormatEmptyMigration(string migrationName) =>
        $"-- Empty migration: {migrationName}{Environment.NewLine}BEGIN;{Environment.NewLine}{Environment.NewLine}-- Add custom SQL here{Environment.NewLine}{Environment.NewLine}COMMIT;{Environment.NewLine}";

    public bool TryStripOuterTransaction(string sql, out string innerSql)
    {
        var span = sql.AsSpan();
        var pos = 0;
        var firstEnd = -1;
        var hasBegin = false;

        while (pos < span.Length)
        {
            var lineStart = pos;
            var nlRel = span[pos..].IndexOfAny('\r', '\n');
            var lineEnd = nlRel < 0 ? span.Length : pos + nlRel;
            var nextPos = lineEnd;
            if (nextPos < span.Length && span[nextPos] == '\r')
            {
                nextPos++;
            }

            if (nextPos < span.Length && span[nextPos] == '\n')
            {
                nextPos++;
            }

            var trimmed = span[lineStart..lineEnd].Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                hasBegin = trimmed.Equals("BEGIN;", StringComparison.OrdinalIgnoreCase);
                firstEnd = nextPos;
                break;
            }

            pos = nextPos;
        }

        if (!hasBegin || firstEnd < 0)
        {
            innerSql = sql;
            return false;
        }

        var endPos = span.Length;
        var lastStart = -1;
        var hasCommit = false;

        while (endPos > firstEnd)
        {
            var lineEnd = endPos;
            if (lineEnd > firstEnd && span[lineEnd - 1] == '\n')
            {
                lineEnd--;
            }

            if (lineEnd > firstEnd && span[lineEnd - 1] == '\r')
            {
                lineEnd--;
            }

            var prevNl = span[..lineEnd].LastIndexOfAny('\r', '\n');
            var lineStart = prevNl < 0 ? 0 : prevNl + 1;
            var trimmed = span[lineStart..lineEnd].Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                hasCommit = trimmed.Equals("COMMIT;", StringComparison.OrdinalIgnoreCase);
                lastStart = lineStart;
                break;
            }

            endPos = lineStart;
        }

        if (hasCommit && lastStart >= firstEnd)
        {
            var body = span[firstEnd..lastStart];
            if (body.EndsWith("\r\n", StringComparison.Ordinal))
            {
                body = body[..^2];
            }
            else if (body.EndsWith("\n", StringComparison.Ordinal) || body.EndsWith("\r", StringComparison.Ordinal))
            {
                body = body[..^1];
            }

            innerSql = body.ToString();
            return true;
        }

        innerSql = sql;
        return false;
    }

    public async Task<IAsyncDisposable?> AcquireLockAsync(
        DbConnection conn,
        DbMigratorOptions options,
        CancellationToken ct)
    {
        var lockId = ComputeLockId(options);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT pg_advisory_lock(@lockId);";
            var p = cmd.CreateParameter();
            p.ParameterName = "@lockId";
            p.Value = lockId;
            cmd.Parameters.Add(p);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return new MigrationDialectRegistry.AsyncDisposableLock(() => ReleaseLockAsync(conn, lockId));
    }

    public async Task EnsureHistoryTableAsync(
        DbConnection conn,
        DbMigratorOptions options,
        CancellationToken ct)
    {
        var table = Qualify(options.HistorySchema, options.HistoryTableName);
        var schemaCreate = !string.IsNullOrWhiteSpace(options.HistorySchema)
            ? $"CREATE SCHEMA IF NOT EXISTS {Quote(options.HistorySchema)};{Environment.NewLine}"
            : string.Empty;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"""
            {schemaCreate}CREATE TABLE IF NOT EXISTS {table} (
                "script_id" uuid NOT NULL,
                "script_name" varchar(512) NOT NULL,
                "checksum_sha256" varchar(64) NOT NULL,
                "applied_at" timestamp with time zone NOT NULL DEFAULT now(),
                CONSTRAINT "pk_{options.HistoryTableName}" PRIMARY KEY ("script_id")
            );
            """;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> HistoryTableExistsAsync(
        DbConnection conn,
        DbMigratorOptions options,
        CancellationToken ct)
    {
        var table = Qualify(options.HistorySchema, options.HistoryTableName);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT to_regclass('{EscapeLiteral(table)}') IS NOT NULL;";
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is true;
    }

    public async Task RecordAppliedAsync(
        DbConnection conn,
        DbMigratorOptions options,
        Guid scriptId,
        string scriptName,
        string checksum,
        CancellationToken ct,
        DbTransaction? transaction = null)
    {
        var table = Qualify(options.HistorySchema, options.HistoryTableName);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText =
            $"INSERT INTO {table} (\"script_id\", \"script_name\", \"checksum_sha256\") VALUES (@id, @script, @sum) ON CONFLICT (\"script_id\") DO NOTHING;";

        var p1 = cmd.CreateParameter();
        p1.ParameterName = "@id";
        p1.Value = scriptId;
        cmd.Parameters.Add(p1);

        var p2 = cmd.CreateParameter();
        p2.ParameterName = "@script";
        p2.Value = scriptName;
        cmd.Parameters.Add(p2);

        var p3 = cmd.CreateParameter();
        p3.ParameterName = "@sum";
        p3.Value = checksum;
        cmd.Parameters.Add(p3);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ReadEfCoreMigrationIdsAsync(DbConnection conn, CancellationToken ct)
    {
        await using (var existsCmd = conn.CreateCommand())
        {
            existsCmd.CommandText = "SELECT to_regclass('\"__EFMigrationsHistory\"') IS NOT NULL;";
            if (await existsCmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true)
            {
                return [];
            }
        }

        var ids = new List<string>();
        await using var readCmd = conn.CreateCommand();
        readCmd.CommandText = "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"";
        await using var reader = await readCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    public async Task<Dictionary<(string Schema, string Table), Dictionary<string, LiveColumnInfo>>> ReadLiveColumnsAsync(
        DbConnection conn,
        IReadOnlyList<(string RegistryName, TableSchema Table)> expectedTables,
        CancellationToken ct)
    {
        var schemas = expectedTables
            .Select(t => NormalizeSchemaName(t.Table.Schema))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var schemaFilter = schemas.Count > 0
            ? $"AND n.nspname IN ({string.Join(", ", schemas.Select(s => $"'{EscapeLiteral(s)}'"))})"
            : "AND n.nspname NOT IN ('pg_catalog', 'information_schema')";

        var result = new Dictionary<(string Schema, string Table), Dictionary<string, LiveColumnInfo>>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT n.nspname, c.relname, a.attname, NOT a.attnotnull, pg_catalog.format_type(a.atttypid, a.atttypmod)
            FROM pg_catalog.pg_attribute a
            JOIN pg_catalog.pg_class c ON c.oid = a.attrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE a.attnum > 0
              AND NOT a.attisdropped
              AND c.relkind IN ('r', 'p')
              {schemaFilter}
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            MigrationDialectRegistry.AddLiveColumn(
                result,
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.GetString(4));
        }

        return result;
    }

    private static long ComputeLockId(DbMigratorOptions options)
    {
        var key = $"SharpPersistence.Migrator:{options.HistorySchema ?? "default"}.{options.HistoryTableName}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return BitConverter.ToInt64(hash, 0);
    }

    private static async Task ReleaseLockAsync(DbConnection conn, long lockId)
    {
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT pg_advisory_unlock(@lockId);";
            var p = cmd.CreateParameter();
            p.ParameterName = "@lockId";
            p.Value = lockId;
            cmd.Parameters.Add(p);
            await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Lock is automatically released when the PostgreSQL session closes.
        }
    }

    internal static string NormalizeTypeName(string sqlType)
    {
        var trimmed = sqlType.Trim();
        var lower = trimmed.ToLowerInvariant().Replace(", ", ",", StringComparison.Ordinal);
        if (lower.StartsWith("varchar(", StringComparison.Ordinal))
        {
            return "character varying" + lower["varchar".Length..];
        }

        if (lower.StartsWith("decimal(", StringComparison.Ordinal))
        {
            return "numeric" + lower["decimal".Length..];
        }

        var normalized = lower switch
        {
            "int" or "int4" or "serial" or "serial4" => "integer",
            "int8" or "bigserial" or "serial8" => "bigint",
            "int2" or "smallserial" or "serial2" => "smallint",
            "bool" => "boolean",
            "float4" => "real",
            "float8" => "double precision",
            "varchar" => "character varying",
            "decimal" => "numeric",
            "timestamptz" => "timestamp with time zone",
            "timestamp" => "timestamp without time zone",
            "timetz" => "time with time zone",
            "time" => "time without time zone",
            _ => null
        };

        if (normalized is not null)
        {
            return normalized;
        }

        if (trimmed.Contains('"', StringComparison.Ordinal))
        {
            var parts = trimmed.Split('.', 2);
            if (parts.Length == 2)
            {
                var schemaPart = UnquoteIfSimpleLowercase(parts[0]);
                var typePart = UnquoteIfSimpleLowercase(parts[1]);
                return string.Equals(schemaPart, "public", StringComparison.Ordinal)
                    ? typePart
                    : $"{schemaPart}.{typePart}";
            }

            return UnquoteIfSimpleLowercase(trimmed);
        }

        return lower;
    }

    public Task<IReadOnlyList<DoctorCheckItem>> CheckPermissionsAsync(
        DbConnection conn,
        DatabasePermissionRequirements requirements,
        CancellationToken ct) =>
        PostgresPermissionChecker.CheckAsync(conn, requirements, ct);

    private static string UnquoteIfSimpleLowercase(string identifier)
    {
        var t = identifier.Trim();
        if (t.Length >= 2 && t[0] == '"' && t[^1] == '"')
        {
            var inner = t[1..^1].Replace("\"\"", "\"");
            var isSimpleLower = inner.Length > 0 &&
                                (char.IsLower(inner[0]) || inner[0] == '_') &&
                                inner.All(c => char.IsLower(c) || char.IsDigit(c) || c == '_');
            return isSimpleLower ? inner : t;
        }

        return t;
    }
}
