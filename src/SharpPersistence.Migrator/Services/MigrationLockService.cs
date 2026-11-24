using System.Data.Common;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;

namespace SharpPersistence.Migrator.Services;

internal sealed class MigrationLockService
{
    private readonly MigrationManagerOptions _options;
    private readonly IMigrationDialect _dialect;

    public MigrationLockService(MigrationManagerOptions options)
    {
        _options = options;
        _dialect = MigrationDialectRegistry.Get(options);
    }

    public Task<IAsyncDisposable?> AcquireAsync(DbConnection conn, CancellationToken ct) =>
        _dialect.AcquireLockAsync(conn, _options, ct);
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
