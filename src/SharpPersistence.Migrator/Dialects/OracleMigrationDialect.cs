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

internal sealed class OracleMigrationDialect(OracleDbCompatibility compatibility = OracleDbCompatibility.OracleDb19)
    : IMigrationDialect
{
    private static readonly Lazy<DbProviderFactory?> CachedFactory = new(() =>
        MigrationDialectRegistry.ResolveProviderFactory(
            "Oracle.ManagedDataAccess.Client.OracleClientFactory",
            "Oracle.ManagedDataAccess"));

    public SqlDialect Dialect => SqlDialect.Oracle;
    public string PingSql => "SELECT 1 FROM DUAL";
    public bool SupportsGlobalExtensionsAndEnums => false;

    public ISqlDialectGenerator CreateDdlGenerator() => new OracleDialectGenerator(compatibility);

    public DbProviderFactory? ResolveDefaultProviderFactory() => CachedFactory.Value;

    public string NormalizeSchemaName(string? schema) =>
        string.IsNullOrWhiteSpace(schema) ? string.Empty : schema.Trim();

    public bool AreSqlTypesCompatible(string depSqlType, string prinSqlType) =>
        string.Equals(depSqlType, prinSqlType, StringComparison.OrdinalIgnoreCase);

    public IEnumerable<string> SplitScriptStatements(string sql) =>
        SplitStatements(sql);

    internal static IEnumerable<string> SplitStatements(string sql)
    {
        var lines = sql.Replace("\r\n", "\n").Split('\n');
        var current = new StringBuilder();
        var inPlSqlBlock = false;

        foreach (var rawLine in lines)
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
        MigrationManagerOptions options,
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

            await cmd.ExecuteNonQueryAsync(ct);

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

                    await Task.Delay(500, ct);
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
        MigrationManagerOptions options,
        CancellationToken ct)
    {
        var table = Qualify(options.HistorySchema, options.HistoryTableName);
        var createSql =
            $"CREATE TABLE {table} (" +
            "\"registry_name\" NVARCHAR2(256) NOT NULL, " +
            "\"script_name\" NVARCHAR2(256) NOT NULL, " +
            "\"checksum_sha256\" VARCHAR2(64) NOT NULL, " +
            "\"applied_at\" TIMESTAMP(7) WITH TIME ZONE DEFAULT SYSTIMESTAMP NOT NULL, " +
            $"CONSTRAINT \"pk_{options.HistoryTableName}\" PRIMARY KEY (\"registry_name\", \"script_name\"))";

        await using var oracleCmd = conn.CreateCommand();
        oracleCmd.CommandText =
            $"BEGIN EXECUTE IMMEDIATE '{EscapeLiteral(createSql)}'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;";
        await oracleCmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> HistoryTableExistsAsync(
        DbConnection conn,
        MigrationManagerOptions options,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = string.IsNullOrWhiteSpace(options.HistorySchema)
            ? $"SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = '{EscapeLiteral(options.HistoryTableName)}'"
            : $"SELECT COUNT(*) FROM ALL_TABLES WHERE OWNER = '{EscapeLiteral(options.HistorySchema)}' AND TABLE_NAME = '{EscapeLiteral(options.HistoryTableName)}'";

        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        return count > 0;
    }

    public async Task RecordAppliedAsync(
        DbConnection conn,
        MigrationManagerOptions options,
        string registryName,
        string scriptName,
        string checksum,
        CancellationToken ct)
    {
        var table = Qualify(options.HistorySchema, options.HistoryTableName);
        await using var cmd = conn.CreateCommand();
        OracleCommandHelper.EnableBindByName(cmd);
        cmd.CommandText =
            $"MERGE INTO {table} t " +
            "USING (SELECT :reg AS \"registry_name\", :script AS \"script_name\", :sum AS \"checksum_sha256\" FROM DUAL) s " +
            "ON (t.\"registry_name\" = s.\"registry_name\" AND t.\"script_name\" = s.\"script_name\") " +
            "WHEN NOT MATCHED THEN INSERT (\"registry_name\", \"script_name\", \"checksum_sha256\") " +
            "VALUES (s.\"registry_name\", s.\"script_name\", s.\"checksum_sha256\")";

        var p1 = cmd.CreateParameter();
        p1.ParameterName = "reg";
        p1.Value = registryName;
        cmd.Parameters.Add(p1);

        var p2 = cmd.CreateParameter();
        p2.ParameterName = "script";
        p2.Value = scriptName;
        cmd.Parameters.Add(p2);

        var p3 = cmd.CreateParameter();
        p3.ParameterName = "sum";
        p3.Value = checksum;
        cmd.Parameters.Add(p3);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<string>> ReadEfCoreMigrationIdsAsync(DbConnection conn, CancellationToken ct)
    {
        await using (var existsCmd = conn.CreateCommand())
        {
            existsCmd.CommandText = "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = '__EFMigrationsHistory'";
            var count = Convert.ToInt32(await existsCmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
            if (count == 0)
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
        var hasUnqualified = expectedTables.Any(t => string.IsNullOrWhiteSpace(t.Table.Schema));
        var explicitSchemas = expectedTables
            .Select(t => t.Table.Schema)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (hasUnqualified)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TABLE_NAME, COLUMN_NAME, NULLABLE FROM USER_TAB_COLUMNS";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                MigrationDialectRegistry.AddLiveColumn(
                    result,
                    string.Empty,
                    reader.GetString(0),
                    reader.GetString(1),
                    string.Equals(reader.GetString(2), "Y", StringComparison.OrdinalIgnoreCase));
            }
        }

        foreach (var owner in explicitSchemas)
        {
            await using var cmd = conn.CreateCommand();
            OracleCommandHelper.EnableBindByName(cmd);
            cmd.CommandText = "SELECT OWNER, TABLE_NAME, COLUMN_NAME, NULLABLE FROM ALL_TAB_COLUMNS WHERE OWNER = :owner";
            var p = cmd.CreateParameter();
            p.ParameterName = "owner";
            p.Value = owner;
            cmd.Parameters.Add(p);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                MigrationDialectRegistry.AddLiveColumn(
                    result,
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    string.Equals(reader.GetString(3), "Y", StringComparison.OrdinalIgnoreCase));
            }
        }

        return result;
    }

    private static int ComputeLockId(MigrationManagerOptions options)
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

            await cmd.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch
        {
            // Lock is automatically released when the Oracle session closes.
        }
    }
}
