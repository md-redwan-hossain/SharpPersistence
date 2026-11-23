using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Abstractions;

public interface IMigrationManager
{
    Task<IReadOnlyList<GeneratedMigrationItem>> GenerateAsync(
        string migrationName,
        string? registryFilter = null,
        bool allowEmpty = false,
        bool interactiveRenamePrompt = true,
        bool dryRun = false,
        Func<string, bool>? confirmRename = null,
        CancellationToken ct = default);

    Task EnsureHistoryTableAsync(CancellationToken ct = default);

    Task<IReadOnlyList<AppliedMigrationRecord>> GetAppliedMigrationsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<MigrationStatusItem>> GetStatusAsync(CancellationToken ct = default);

    Task<IReadOnlyList<string>> MigrateAsync(bool dryRun = false, CancellationToken ct = default);

    Task<IReadOnlyList<string>> SyncEfCoreHistoryAsync(
        bool dryRun = false,
        CancellationToken ct = default);

    Task<IReadOnlyList<SchemaDriftItem>> VerifySchemaAsync(CancellationToken ct = default);
}
