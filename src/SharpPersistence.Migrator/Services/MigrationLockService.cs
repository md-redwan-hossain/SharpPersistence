using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Configuration;

namespace SharpPersistence.Migrator.Services;

internal sealed class MigrationLockService
{
    private readonly MigrationManagerOptions _options;

    public MigrationLockService(MigrationManagerOptions options)
    {
        _options = options;
    }

    public async Task<IAsyncDisposable?> AcquireAsync(DbConnection conn, CancellationToken ct)
    {
        if (_options.Dialect == SqlDialect.PostgreSql)
        {
            var lockId = ComputePostgresLockId();
            await AcquirePostgresAdvisoryLockAsync(conn, lockId, ct);
            return new AsyncDisposableLock(() => ReleasePostgresAdvisoryLockAsync(conn, lockId));
        }

        if (_options.Dialect == SqlDialect.Oracle)
        {
            var lockId = ComputeOracleLockId();
            await AcquireOracleLockAsync(conn, lockId, ct: ct);
            return new AsyncDisposableLock(() => ReleaseOracleLockAsync(conn, lockId));
        }

        return null;
    }

    private long ComputePostgresLockId()
    {
        var key = $"SharpPersistence.Migrator:{_options.HistorySchema ?? "default"}.{_options.HistoryTableName}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return BitConverter.ToInt64(hash, 0);
    }

    private int ComputeOracleLockId()
    {
        var key = $"SharpPersistence.Migrator:{_options.HistorySchema ?? "default"}.{_options.HistoryTableName}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return BitConverter.ToInt32(hash, 0) & 0x3FFFFFFF;
    }

    private static async Task AcquirePostgresAdvisoryLockAsync(DbConnection conn, long lockId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT pg_advisory_lock(@lockId);";
        var p = cmd.CreateParameter();
        p.ParameterName = "@lockId";
        p.Value = lockId;
        cmd.Parameters.Add(p);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task ReleasePostgresAdvisoryLockAsync(DbConnection conn, long lockId)
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

    private static async Task AcquireOracleLockAsync(
        DbConnection conn,
        int lockId,
        TimeSpan? maxWait = null,
        CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.UtcNow + (maxWait ?? TimeSpan.FromMinutes(5));

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
                    return;
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

    private static async Task ReleaseOracleLockAsync(DbConnection conn, int lockId)
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

    private sealed class AsyncDisposableLock(Func<Task> release) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await release();
    }
}

internal static class OracleCommandHelper
{
    public static void EnableBindByName(DbCommand cmd)
    {
        var prop = cmd.GetType().GetProperty("BindByName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (prop is not null && prop.CanWrite && prop.PropertyType == typeof(bool))
        {
            prop.SetValue(cmd, true);
        }
    }
}
