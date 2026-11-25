using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Abstractions;

public interface IDbMigrator
{
    Task EnsureHistoryTableAsync(CancellationToken ct = default);

    Task<IReadOnlyList<AppliedMigrationRecord>> GetAppliedMigrationsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<MigrationStatusItem>> GetStatusAsync(CancellationToken ct = default);

    Task<IReadOnlyList<string>> MigrateAsync(bool dryRun = false, CancellationToken ct = default);
}
