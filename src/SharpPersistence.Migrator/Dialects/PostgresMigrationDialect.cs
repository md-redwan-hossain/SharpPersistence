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
    private static readonly Lazy<DbProviderFactory?> CachedFactory = new(() =>
        MigrationDialectRegistry.ResolveProviderFactory("Npgsql.NpgsqlFactory", "Npgsql"));

    public SqlDialect Dialect => SqlDialect.PostgreSql;
    public string PingSql => "SELECT 1";
    public bool SupportsGlobalExtensionsAndEnums => true;
    public bool SupportsComputedStorageMode => true;
    public bool SupportsTransactionalDdl => true;
    public string ProviderPackageHint => "Install the Npgsql NuGet package or pass a DbProviderFactory / connection factory.";

    public ISqlDialectGenerator CreateDdlGenerator() => new PostgresDialectGenerator();

    public DbProviderFactory? ResolveDefaultProviderFactory() => CachedFactory.Value;

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
        var lines = sql.Replace("\r\n", "\n").Split('\n');
        var firstIdx = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            firstIdx = i;
            break;
        }

        var lastIdx = -1;
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            lastIdx = i;
            break;
        }

        if (firstIdx >= 0 && lastIdx > firstIdx &&
            string.Equals(lines[firstIdx].Trim(), "BEGIN;", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(lines[lastIdx].Trim(), "COMMIT;", StringComparison.OrdinalIgnoreCase))
        {
            innerSql = string.Join(Environment.NewLine, lines.Skip(firstIdx + 1).Take(lastIdx - firstIdx - 1));
            return true;
        }

        innerSql = sql;
        return false;
    }

    public async Task<IAsyncDisposable?> AcquireLockAsync(
        DbConnection conn,
        MigrationManagerOptions options,
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
            await cmd.ExecuteNonQueryAsync(ct);
        }

        return new MigrationDialectRegistry.AsyncDisposableLock(() => ReleaseLockAsync(conn, lockId));
    }

    public async Task EnsureHistoryTableAsync(
        DbConnection conn,
        MigrationManagerOptions options,
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
                "registry_name" text NOT NULL,
                "script_name" text NOT NULL,
                "checksum_sha256" text NOT NULL,
                "applied_at" timestamp with time zone NOT NULL DEFAULT now(),
                CONSTRAINT "pk_{options.HistoryTableName}" PRIMARY KEY ("registry_name", "script_name")
            );
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> HistoryTableExistsAsync(
        DbConnection conn,
        MigrationManagerOptions options,
        CancellationToken ct)
    {
        var table = Qualify(options.HistorySchema, options.HistoryTableName);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT to_regclass('{EscapeLiteral(table)}') IS NOT NULL;";
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is true;
    }

    public async Task RecordAppliedAsync(
        DbConnection conn,
        MigrationManagerOptions options,
        string registryName,
        string scriptName,
        string checksum,
        CancellationToken ct,
        DbTransaction? transaction = null)
    {
        var table = Qualify(options.HistorySchema, options.HistoryTableName);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText =
            $"INSERT INTO {table} (\"registry_name\", \"script_name\", \"checksum_sha256\") VALUES (@reg, @script, @sum) ON CONFLICT (\"registry_name\", \"script_name\") DO NOTHING;";

        var p1 = cmd.CreateParameter();
        p1.ParameterName = "@reg";
        p1.Value = registryName;
        cmd.Parameters.Add(p1);

        var p2 = cmd.CreateParameter();
        p2.ParameterName = "@script";
        p2.Value = scriptName;
        cmd.Parameters.Add(p2);

        var p3 = cmd.CreateParameter();
        p3.ParameterName = "@sum";
        p3.Value = checksum;
        cmd.Parameters.Add(p3);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<string>> ReadEfCoreMigrationIdsAsync(DbConnection conn, CancellationToken ct)
    {
        await using (var existsCmd = conn.CreateCommand())
        {
            existsCmd.CommandText = "SELECT to_regclass('\"__EFMigrationsHistory\"') IS NOT NULL;";
            if (await existsCmd.ExecuteScalarAsync(ct) is not true)
            {
                return [];
            }
        }

        var ids = new List<string>();
        await using var readCmd = conn.CreateCommand();
        readCmd.CommandText = "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"";
        await using var reader = await readCmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    public async Task<Dictionary<(string Schema, string Table), Dictionary<string, bool>>> ReadLiveColumnsAsync(
        DbConnection conn,
        IReadOnlyList<(string RegistryName, TableSchema Table)> expectedTables,
        CancellationToken ct)
    {
        var result = new Dictionary<(string Schema, string Table), Dictionary<string, bool>>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT table_schema, table_name, column_name, is_nullable
            FROM information_schema.columns
            WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            MigrationDialectRegistry.AddLiveColumn(
                result,
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                string.Equals(reader.GetString(3), "YES", StringComparison.OrdinalIgnoreCase));
        }

        return result;
    }

    private static long ComputeLockId(MigrationManagerOptions options)
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
            await cmd.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch
        {
            // Lock is automatically released when the PostgreSQL session closes.
        }
    }

    internal static string NormalizeTypeName(string sqlType)
    {
        var trimmed = sqlType.Trim();
        var lower = trimmed.ToLowerInvariant();
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
