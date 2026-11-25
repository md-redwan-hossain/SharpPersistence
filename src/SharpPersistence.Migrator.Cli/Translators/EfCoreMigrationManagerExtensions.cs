using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Models;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Cli.Translators;

internal static class EfCoreMigrationManagerExtensions
{
    public static async Task<IReadOnlyList<GeneratedMigrationItem>> TranslateEfCoreMigrationsAsync(
        this IDbMigrator manager,
        string efCoreMigrationsDirectory,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manager);

        if (manager is not DbMigrator concrete)
        {
            throw new InvalidOperationException(
                $"TranslateEfCoreMigrationsAsync requires the default '{nameof(DbMigrator)}' implementation.");
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
        var scriptIds = new List<Guid>(translated.Count);
        foreach (var item in translated)
        {
            var (stampedSql, scriptId, _) = MigrationScriptHeader.StampHeader(item.SqlScript);
            var scriptPath = Path.Combine(legacyDir, $"{item.MigrationId}.sql");
            if (!dryRun)
            {
                await File.WriteAllTextAsync(scriptPath, stampedSql, ct);
            }

            scriptIds.Add(scriptId);
            items.Add(new GeneratedMigrationItem(
                "_translated",
                scriptPath,
                null,
                stampedSql,
                string.Empty,
                false));
        }

        if (!dryRun && scriptIds.Count > 0)
        {
            await concrete.CreateRunlistManager().AppendNewScriptsAsync(scriptIds, ct);
        }

        return items;
    }
}
