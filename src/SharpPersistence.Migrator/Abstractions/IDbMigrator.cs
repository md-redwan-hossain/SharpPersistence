using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Abstractions;

public interface IDbMigrator
{
    Task EnsureHistoryTableAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppliedMigrationRecord>> GetAppliedMigrationsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MigrationStatusItem>> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> MigrateAsync(bool dryRun = false, CancellationToken cancellationToken = default);
}
