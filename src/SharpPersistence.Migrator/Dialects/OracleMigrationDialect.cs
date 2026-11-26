using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.Models;
using SharpPersistence.Migrator.Services;
using static SharpPersistence.Migrator.Dialects.SqlFormatHelper;

namespace SharpPersistence.Migrator.Dialects;

internal sealed class OracleMigrationDialect(OracleDbCompatibility compatibility = OracleDbCompatibility.Oracle19)
    : IMigrationDialect
{
    private static DbProviderFactory? _cachedFactory;

    public SqlDialect Dialect => SqlDialect.Oracle;
    public string PingSql => "SELECT 1 FROM DUAL";
    public bool SupportsGlobalExtensionsAndEnums => false;
    public bool SupportsComputedStorageMode => false;
    public bool SupportsTransactionalDdl => false;
    public string ProviderPackageHint => "Install Oracle.ManagedDataAccess.Core or pass a DbProviderFactory / connection factory.";

    public ISqlDialectGenerator CreateDdlGenerator() => new OracleDialectGenerator(compatibility);

    public DbProviderFactory? ResolveDefaultProviderFactory() =>
        _cachedFactory ??= MigrationDialectRegistry.ResolveProviderFactory(
            "Oracle.ManagedDataAccess.Client.OracleClientFactory",
            "Oracle.ManagedDataAccess");

    public string NormalizeSchemaName(string? schema) =>
        string.IsNullOrWhiteSpace(schema) ? string.Empty : schema.Trim();

    public bool AreSqlTypesCompatible(string depSqlType, string prinSqlType) =>
        string.Equals(depSqlType, prinSqlType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(NormalizeTypeName(depSqlType), NormalizeTypeName(prinSqlType), StringComparison.OrdinalIgnoreCase);

    internal static string NormalizeTypeName(string sqlType)
    {
        var normalized = sqlType.Trim().ToUpperInvariant()
            .Replace(", ", ",", StringComparison.Ordinal)
            .Replace(" CHAR)", ")", StringComparison.Ordinal)
            .Replace(" BYTE)", ")", StringComparison.Ordinal);

        if (normalized.EndsWith(",0)", StringComparison.Ordinal) &&
            normalized.StartsWith("NUMBER(", StringComparison.Ordinal))
        {
            normalized = normalized[..^3] + ")";
        }

        return normalized switch
        {
            "INT" or "INTEGER" => "NUMBER(38)",
            _ => normalized
        };
    }

    public string ResolveSequenceSqlType(Type clrType) => "NUMBER";

    public IEnumerable<string> SplitScriptStatements(string sql) =>
        SplitStatements(sql);

    public string FormatEmptyMigration(string migrationName) =>
        $"-- Empty migration: {migrationName}{Environment.NewLine}-- Add custom SQL here{Environment.NewLine}";

    public bool TryStripOuterTransaction(string sql, out string innerSql)
    {
        innerSql = sql;
        return false;
    }

    internal static IEnumerable<string> SplitStatements(string sql)
    {
        using var reader = new StringReader(sql);
        var current = new StringBuilder();
        var inPlSqlBlock = false;

        while (reader.ReadLine() is { } rawLine)
        {
            var trimmed = rawLine.Trim();
            if (trimmed.StartsWith("--", StringComparison.Ordinal) || string.IsNullOrEmpty(trimmed))
            {
                if (inPlSqlBlock)
                {
                    current.AppendLine(rawLine);
                }

                continue;
            }

            if (!inPlSqlBlock && current.Length == 0 &&
                (trimmed.StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.StartsWith("DECLARE", StringComparison.OrdinalIgnoreCase)))
            {
                inPlSqlBlock = true;
            }

            if (inPlSqlBlock)
            {
                if (trimmed == "/")
                {
                    var block = current.ToString().Trim();
                    current.Clear();
                    inPlSqlBlock = false;
                    if (!string.IsNullOrWhiteSpace(block))
                    {
                        yield return block;
                    }
                }
                else
                {
                    current.AppendLine(rawLine);
                }
            }
            else
            {
                current.AppendLine(rawLine);
                if (trimmed.EndsWith(';'))
                {
                    var stmt = current.ToString().Trim().TrimEnd(';').Trim();
                    current.Clear();
                    if (!string.IsNullOrWhiteSpace(stmt))
                    {
                        yield return stmt;
                    }
                }
            }
        }

        var tail = current.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(tail))
        {
            yield return inPlSqlBlock ? tail : tail.TrimEnd(';').Trim();
        }
    }

    public async Task<IAsyncDisposable?> AcquireLockAsync(
        DbConnection conn,
        DbMigratorOptions options,
        CancellationToken ct)
    {
        var lockId = ComputeLockId(options);
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(5);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            await using var cmd = conn.CreateCommand();
            OracleCommandHelper.EnableBindByName(cmd);
            cmd.CommandText = """
                DECLARE
                    v_res INTEGER;
                BEGIN
                    v_res := DBMS_LOCK.REQUEST(
                        id                => :lockId,
                        lockmode          => 6,
                        timeout           => 1,
                        release_on_commit => FALSE
                    );
                    :res := v_res;
                END;
                """;

            var pId = cmd.CreateParameter();
            pId.ParameterName = "lockId";
            pId.DbType = DbType.Int32;
            pId.Value = lockId;
            cmd.Parameters.Add(pId);

            var pRes = cmd.CreateParameter();
            pRes.ParameterName = "res";
            pRes.DbType = DbType.Int32;
            pRes.Direction = ParameterDirection.Output;
            cmd.Parameters.Add(pRes);

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            var res = Convert.ToInt32(pRes.Value, CultureInfo.InvariantCulture);
            switch (res)
            {
                case 0:
                case 4:
                    return new MigrationDialectRegistry.AsyncDisposableLock(() => ReleaseLockAsync(conn, lockId));
                case 1:
                    if (DateTimeOffset.UtcNow >= deadline)
                    {
                        throw new TimeoutException($"Timed out waiting for Oracle migration lock (id={lockId}).");
                    }

                    await Task.Delay(500, ct).ConfigureAwait(false);
                    break;
                case 2:
                    throw new InvalidOperationException($"Deadlock detected acquiring Oracle migration lock (id={lockId}).");
                default:
                    throw new InvalidOperationException($"DBMS_LOCK.REQUEST failed with code {res} (id={lockId}).");
            }
        }
    }

    public async Task EnsureHistoryTableAsync(
        DbConnection conn,
        DbMigratorOptions options,
        CancellationToken ct)
    {
        var table = Qualify(options.HistorySchema, options.HistoryTableName);
        var createSql =
            $"CREATE TABLE {table} (" +
            "\"script_id\" VARCHAR2(36) NOT NULL, " +
            "\"script_name\" NVARCHAR2(512) NOT NULL, " +
            "\"checksum_sha256\" VARCHAR2(64) NOT NULL, " +
            "\"applied_at\" TIMESTAMP(7) WITH TIME ZONE DEFAULT SYSTIMESTAMP NOT NULL, " +
            $"CONSTRAINT \"pk_{options.HistoryTableName}\" PRIMARY KEY (\"script_id\"))";

        await using var oracleCmd = conn.CreateCommand();
        oracleCmd.CommandText =
            $"BEGIN EXECUTE IMMEDIATE '{EscapeLiteral(createSql)}'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;";
        await oracleCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> HistoryTableExistsAsync(
        DbConnection conn,
        DbMigratorOptions options,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = string.IsNullOrWhiteSpace(options.HistorySchema)
            ? $"SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = '{EscapeLiteral(options.HistoryTableName)}'"
            : $"SELECT COUNT(*) FROM ALL_TABLES WHERE OWNER = '{EscapeLiteral(options.HistorySchema)}' AND TABLE_NAME = '{EscapeLiteral(options.HistoryTableName)}'";

        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
        return count > 0;
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
        OracleCommandHelper.EnableBindByName(cmd);
        cmd.CommandText =
            $"MERGE INTO {table} t " +
            "USING (SELECT :id AS \"script_id\", :script AS \"script_name\", :sum AS \"checksum_sha256\" FROM DUAL) s " +
            "ON (t.\"script_id\" = s.\"script_id\") " +
            "WHEN NOT MATCHED THEN INSERT (\"script_id\", \"script_name\", \"checksum_sha256\") " +
            "VALUES (s.\"script_id\", s.\"script_name\", s.\"checksum_sha256\")";

        var p1 = cmd.CreateParameter();
        p1.ParameterName = "id";
        p1.Value = scriptId.ToString("N");
        cmd.Parameters.Add(p1);

        var p2 = cmd.CreateParameter();
        p2.ParameterName = "script";
        p2.Value = scriptName;
        cmd.Parameters.Add(p2);

        var p3 = cmd.CreateParameter();
        p3.ParameterName = "sum";
        p3.Value = checksum;
        cmd.Parameters.Add(p3);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ReadEfCoreMigrationIdsAsync(DbConnection conn, CancellationToken ct)
    {
        await using (var existsCmd = conn.CreateCommand())
        {
            existsCmd.CommandText = "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = '__EFMigrationsHistory'";
            var count = Convert.ToInt32(await existsCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
            if (count == 0)
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
        var result = new Dictionary<(string Schema, string Table), Dictionary<string, LiveColumnInfo>>();
        var hasUnqualified = expectedTables.Any(t => string.IsNullOrWhiteSpace(t.Table.Schema));
        var explicitSchemas = expectedTables
            .Select(t => t.Table.Schema)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (hasUnqualified)
        {
            var unqualifiedTables = expectedTables
                .Where(t => string.IsNullOrWhiteSpace(t.Table.Schema))
                .Select(t => t.Table.TableName)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = unqualifiedTables.Count is > 0 and <= 1000
                ? $"SELECT TABLE_NAME, COLUMN_NAME, NULLABLE, DATA_TYPE, CHAR_LENGTH, DATA_PRECISION, DATA_SCALE FROM USER_TAB_COLUMNS WHERE TABLE_NAME IN ({string.Join(", ", unqualifiedTables.Select(t => $"'{EscapeLiteral(t)}'"))})"
                : "SELECT TABLE_NAME, COLUMN_NAME, NULLABLE, DATA_TYPE, CHAR_LENGTH, DATA_PRECISION, DATA_SCALE FROM USER_TAB_COLUMNS";
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                MigrationDialectRegistry.AddLiveColumn(
                    result,
                    string.Empty,
                    reader.GetString(0),
                    reader.GetString(1),
                    string.Equals(reader.GetString(2), "Y", StringComparison.OrdinalIgnoreCase),
                    FormatOracleLiveType(reader, 3));
            }
        }

        foreach (var owner in explicitSchemas)
        {
            await using var cmd = conn.CreateCommand();
            OracleCommandHelper.EnableBindByName(cmd);
            cmd.CommandText = "SELECT OWNER, TABLE_NAME, COLUMN_NAME, NULLABLE, DATA_TYPE, CHAR_LENGTH, DATA_PRECISION, DATA_SCALE FROM ALL_TAB_COLUMNS WHERE OWNER = :owner";
            var p = cmd.CreateParameter();
            p.ParameterName = "owner";
            p.Value = owner;
            cmd.Parameters.Add(p);

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                MigrationDialectRegistry.AddLiveColumn(
                    result,
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    string.Equals(reader.GetString(3), "Y", StringComparison.OrdinalIgnoreCase),
                    FormatOracleLiveType(reader, 4));
            }
        }

        return result;
    }

    private static string FormatOracleLiveType(DbDataReader reader, int dataTypeOrdinal)
    {
        var dataType = reader.GetString(dataTypeOrdinal);
        var charLen = reader.IsDBNull(dataTypeOrdinal + 1) ? (int?)null : Convert.ToInt32(reader.GetValue(dataTypeOrdinal + 1));
        var precision = reader.IsDBNull(dataTypeOrdinal + 2) ? (int?)null : Convert.ToInt32(reader.GetValue(dataTypeOrdinal + 2));
        var scale = reader.IsDBNull(dataTypeOrdinal + 3) ? (int?)null : Convert.ToInt32(reader.GetValue(dataTypeOrdinal + 3));

        if (dataType is "VARCHAR2" or "NVARCHAR2" or "CHAR" or "NCHAR" && charLen is > 0)
        {
            return $"{dataType}({charLen.Value})";
        }

        if (string.Equals(dataType, "NUMBER", StringComparison.OrdinalIgnoreCase) && precision.HasValue)
        {
            return scale is > 0
                ? $"NUMBER({precision.Value},{scale.Value})"
                : $"NUMBER({precision.Value})";
        }

        return dataType;
    }

    private static int ComputeLockId(DbMigratorOptions options)
    {
        var key = $"SharpPersistence.Migrator:{options.HistorySchema ?? "default"}.{options.HistoryTableName}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return BitConverter.ToInt32(hash, 0) & 0x3FFFFFFF;
    }

    private static async Task ReleaseLockAsync(DbConnection conn, int lockId)
    {
        try
        {
            await using var cmd = conn.CreateCommand();
            OracleCommandHelper.EnableBindByName(cmd);
            cmd.CommandText = """
                DECLARE
                    v_res INTEGER;
                BEGIN
                    v_res := DBMS_LOCK.RELEASE(id => :lockId);
                END;
                """;

            var pId = cmd.CreateParameter();
            pId.ParameterName = "lockId";
            pId.DbType = DbType.Int32;
            pId.Value = lockId;
            cmd.Parameters.Add(pId);

            await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Lock is automatically released when the Oracle session closes.
        }
    }
}

internal static class OracleCommandHelper
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.PropertyInfo?> BindByNameCache = new();

    public static void EnableBindByName(DbCommand cmd)
    {
        var prop = BindByNameCache.GetOrAdd(cmd.GetType(), static type =>
        {
            var p = type.GetProperty("BindByName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            return p is not null && p.CanWrite && p.PropertyType == typeof(bool) ? p : null;
        });

        prop?.SetValue(cmd, true);
    }
}
