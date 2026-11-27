using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Abstractions;

public interface IDbMigrator
{
    Task<bool> CanConnectToDatabaseAsync(CancellationToken cancellationToken = default);

    Task<bool> HasPendingMigrationAsync(CancellationToken cancellationToken = default);

    Task EnsureHistoryTableAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppliedMigrationRecord>> GetAppliedMigrationsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MigrationStatusItem>> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> MigrateAsync(
        bool dryRun = false,
        bool skipPermissionCheck = false,
        CancellationToken cancellationToken = default);
}
