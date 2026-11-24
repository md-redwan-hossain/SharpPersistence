using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Models;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Cli.Translators;

internal static class EfCoreMigrationManagerExtensions
{
    public static async Task<IReadOnlyList<GeneratedMigrationItem>> TranslateEfCoreMigrationsAsync(
        this IMigrationManager manager,
        string efCoreMigrationsDirectory,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manager);

        if (manager is not MigrationManager concrete)
        {
            throw new InvalidOperationException(
                $"TranslateEfCoreMigrationsAsync requires the default '{nameof(MigrationManager)}' implementation.");
        }

        var translated = EfCoreMigrationTranslator.TranslateFromDirectory(
            efCoreMigrationsDirectory,
            concrete.Options.Dialect,
            concrete.Options.OracleCompatibility);

        if (translated.Count == 0)
        {
            return [];
        }

        var legacyDir = Path.Combine(concrete.ResolveMigrationsDirectory(), "_translated");
        if (!dryRun)
        {
            Directory.CreateDirectory(legacyDir);
        }

        var items = new List<GeneratedMigrationItem>(translated.Count);
        foreach (var item in translated)
        {
            var scriptPath = Path.Combine(legacyDir, $"{item.MigrationId}.sql");
            if (!dryRun)
            {
                await File.WriteAllTextAsync(scriptPath, item.SqlScript, ct);
            }

            items.Add(new GeneratedMigrationItem(
                "_translated",
                scriptPath,
                null,
                item.SqlScript,
                string.Empty,
                false));
        }

        return items;
    }
}
