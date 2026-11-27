using System.Globalization;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Diffing;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal sealed class MigrationScriptGenerator(
    DbMigratorOptions options,
    IMigrationDialect dialect,
    MigrationScriptStore scriptStore)
{
    public async Task<IReadOnlyList<GeneratedMigrationItem>> GenerateAsync(
        string migrationName,
        string? registryFilter = null,
        bool allowEmpty = false,
        bool interactiveRenamePrompt = true,
        bool dryRun = false,
        Func<string, bool>? confirmRename = null,
        CancellationToken ct = default)
    {
        var stateDir = scriptStore.ResolveSnapshotDirectory();
        var sqlRootDir = scriptStore.ResolveMigrationsDirectory();
        var deferredDir = Path.Combine(sqlRootDir, "_deferred");

        if (!dryRun)
        {
            new MigrationRunlistManager(options, scriptStore).EnsureReadyForAppend();
            Directory.CreateDirectory(stateDir);
            Directory.CreateDirectory(sqlRootDir);
        }

        var sortedRegistries = RegistryDependencyResolver.SortTopologically(options);

        var targets = string.IsNullOrWhiteSpace(registryFilter)
            ? sortedRegistries
            : sortedRegistries.Where(r =>
                string.Equals(r.GetType().Name, registryFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        if (targets.Count == 0)
        {
            return [];
        }

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var generated = new List<GeneratedMigrationItem>();
        var generatedScriptIds = new List<Guid>();
        var deferredScriptIds = new List<Guid>();

        if (dialect.SupportsGlobalExtensionsAndEnums && string.IsNullOrWhiteSpace(registryFilter))
        {
            var globalItem = await GenerateGlobalObjectsAsync(migrationName, timestamp, stateDir, sqlRootDir, dryRun, ct).ConfigureAwait(false);
            if (globalItem is { } g)
            {
                generated.Add(g.Item);
                generatedScriptIds.Add(g.ScriptId);
            }
        }

        var orphanedSnapshots = await LoadOrphanedSnapshotsAsync(stateDir, sortedRegistries, ct).ConfigureAwait(false);
        var planned = new List<(string RegistryName, string SnapshotPath, string? RenamedFromPath, SchemaDiffResult Diff)>(targets.Count);

        foreach (var registry in targets)
        {
            var registryName = registry.GetType().Name;
            var snapshotPath = Path.Combine(stateDir, $"{registryName}.json");
            string? previousJson = null;
            string? renamedFromPath = null;

            var currentSchema = SchemaScanner.ScanRegistry(registry, options, sortedRegistries);

            if (File.Exists(snapshotPath))
            {
                previousJson = await File.ReadAllTextAsync(snapshotPath, ct).ConfigureAwait(false);
            }
            else if (orphanedSnapshots.Count > 0)
            {
                var match = FindMatchingOrphanSnapshot(orphanedSnapshots, currentSchema);
                if (match is { } orphan)
                {
                    var matchedList = string.Join(", ", orphan.MatchedTables);
                    if (interactiveRenamePrompt && confirmRename is not null)
                    {
                        if (confirmRename($"Was registry '{orphan.RegistryName}' renamed to '{registryName}'? (Matched table(s): {matchedList})"))
                        {
                            previousJson = orphan.Json;
                            renamedFromPath = orphan.FilePath;
                            orphanedSnapshots.Remove(orphan.RegistryName);
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"Registry '{registryName}' has no snapshot, but orphaned snapshot '{orphan.RegistryName}.json' already tracks table(s): {matchedList}. " +
                            $"If you renamed the registry, rename '{orphan.FilePath}' to '{snapshotPath}' and re-run.");
                    }
                }
            }

            var result = StandaloneSchemaDiffer.GenerateDiff(
                currentSchema,
                previousJson,
                options.Dialect,
                interactiveRenamePrompt,
                options.Oracle.Compatibility,
                confirmRename);

            planned.Add((registryName, snapshotPath, renamedFromPath, result));
        }

        DetectCrossRegistryTableMoves(planned);

        foreach (var (registryName, snapshotPath, renamedFromPath, result) in planned)
        {
            if (!result.HasChanges)
            {
                if (renamedFromPath is not null)
                {
                    if (!dryRun)
                    {
                        File.Move(renamedFromPath, snapshotPath, overwrite: true);
                    }

                    generated.Add(new GeneratedMigrationItem(
                        registryName, null, null, string.Empty, string.Empty, false, snapshotPath, renamedFromPath));
                }

                if (!allowEmpty)
                {
                    continue;
                }

                var emptyId = Guid.CreateVersion7();
                var emptyDir = Path.Combine(sqlRootDir, registryName);
                var emptySqlPath = Path.Combine(emptyDir, MigrationScriptHeader.FormatScriptFileName(emptyId, timestamp, migrationName));
                var emptySql = dialect.FormatEmptyMigration(migrationName);

                if (!dryRun)
                {
                    Directory.CreateDirectory(emptyDir);
                    await File.WriteAllTextAsync(emptySqlPath, emptySql, ct).ConfigureAwait(false);
                }

                generated.Add(new GeneratedMigrationItem(registryName, emptySqlPath, null, emptySql, string.Empty, true));
                generatedScriptIds.Add(emptyId);
                continue;
            }

            string? mainPath = null;
            string? deferredPath = null;
            var mainSql = result.SqlScript;
            var deferredSql = result.DeferredSqlScript;

            if (!string.IsNullOrWhiteSpace(mainSql))
            {
                var mainId = Guid.CreateVersion7();
                var registrySqlDir = Path.Combine(sqlRootDir, registryName);
                mainPath = Path.Combine(registrySqlDir, MigrationScriptHeader.FormatScriptFileName(mainId, timestamp, migrationName));
                if (!dryRun)
                {
                    Directory.CreateDirectory(registrySqlDir);
                    await File.WriteAllTextAsync(mainPath, mainSql, ct).ConfigureAwait(false);
                }

                generatedScriptIds.Add(mainId);
            }

            if (!string.IsNullOrWhiteSpace(deferredSql))
            {
                var deferredId = Guid.CreateVersion7();
                deferredPath = Path.Combine(deferredDir, MigrationScriptHeader.FormatScriptFileName(deferredId, timestamp, migrationName));
                if (!dryRun)
                {
                    Directory.CreateDirectory(deferredDir);
                    await File.WriteAllTextAsync(deferredPath, deferredSql, ct).ConfigureAwait(false);
                }

                deferredScriptIds.Add(deferredId);
            }

            if (!dryRun)
            {
                await File.WriteAllTextAsync(snapshotPath, result.UpdatedSnapshotJson, ct).ConfigureAwait(false);
                if (renamedFromPath is not null && File.Exists(renamedFromPath))
                {
                    File.Delete(renamedFromPath);
                }
            }

            generated.Add(new GeneratedMigrationItem(
                registryName,
                mainPath,
                deferredPath,
                mainSql,
                deferredSql,
                false,
                snapshotPath,
                renamedFromPath));
        }

        if (!dryRun && (generatedScriptIds.Count > 0 || deferredScriptIds.Count > 0))
        {
            var runlist = new MigrationRunlistManager(options, scriptStore);
            var allNewIds = new List<Guid>(generatedScriptIds.Count + deferredScriptIds.Count);
            allNewIds.AddRange(generatedScriptIds);
            allNewIds.AddRange(deferredScriptIds);
            await runlist.AppendNewScriptsAsync(allNewIds, ct).ConfigureAwait(false);
        }

        return generated;
    }

    private async Task<(GeneratedMigrationItem Item, Guid ScriptId)?> GenerateGlobalObjectsAsync(
        string migrationName,
        string timestamp,
        string stateDir,
        string sqlRootDir,
        bool dryRun,
        CancellationToken ct)
    {
        var globalSnapshotPath = Path.Combine(stateDir, "_global.json");
        var prevGlobalJson = File.Exists(globalSnapshotPath)
            ? await File.ReadAllTextAsync(globalSnapshotPath, ct).ConfigureAwait(false)
            : null;

        var globalSchema = new DatabaseModel
        {
            Extensions = options.PostgreSql.Extensions.Count > 0 ? [.. options.PostgreSql.Extensions] : null,
            Enums = options.PostgreSql.Enums.Count > 0 ? [.. options.PostgreSql.Enums] : null
        };

        if (prevGlobalJson is null &&
            globalSchema.Extensions is not { Count: > 0 } &&
            globalSchema.Enums is not { Count: > 0 })
        {
            return null;
        }

        var globalDiff = StandaloneSchemaDiffer.GenerateDiff(
            globalSchema,
            prevGlobalJson,
            options.Dialect,
            interactiveRenamePrompt: false);

        if (!globalDiff.HasChanges)
        {
            return null;
        }

        string? globalSqlPath = null;
        var globalSql = globalDiff.SqlScript;
        var globalId = Guid.Empty;
        if (!string.IsNullOrWhiteSpace(globalSql))
        {
            globalId = Guid.CreateVersion7();
            var globalDir = Path.Combine(sqlRootDir, "_global");
            globalSqlPath = Path.Combine(globalDir, MigrationScriptHeader.FormatScriptFileName(globalId, timestamp, migrationName));
            if (!dryRun)
            {
                Directory.CreateDirectory(globalDir);
                await File.WriteAllTextAsync(globalSqlPath, globalSql, ct).ConfigureAwait(false);
            }
        }

        if (!dryRun)
        {
            await File.WriteAllTextAsync(globalSnapshotPath, globalDiff.UpdatedSnapshotJson, ct).ConfigureAwait(false);
        }

        return (new GeneratedMigrationItem(
            "_global",
            globalSqlPath,
            null,
            globalSql,
            string.Empty,
            false,
            globalSnapshotPath), globalId);
    }

    private static async Task<Dictionary<string, (string FilePath, string Json, DatabaseModel Model)>> LoadOrphanedSnapshotsAsync(
        string stateDir,
        IReadOnlyList<IEntityRegistry> activeRegistries,
        CancellationToken ct)
    {
        var result = new Dictionary<string, (string, string, DatabaseModel)>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(stateDir))
        {
            return result;
        }

        var activeNames = activeRegistries
            .Select(r => r.GetType().Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(stateDir, "*.json", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (string.Equals(name, "_global", StringComparison.OrdinalIgnoreCase) || activeNames.Contains(name))
            {
                continue;
            }

            var json = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
            var model = StandaloneSchemaDiffer.DeserializeSnapshot(json);
            if (model is not null && model.Tables.Count > 0)
            {
                result[name] = (file, json, model);
            }
        }

        return result;
    }

    private static (string RegistryName, string FilePath, string Json, List<string> MatchedTables)? FindMatchingOrphanSnapshot(
        Dictionary<string, (string FilePath, string Json, DatabaseModel Model)> orphans,
        DatabaseModel currentSchema)
    {
        var currentQualified = currentSchema.Tables.Select(t => t.QualifiedName).ToHashSet(StringComparer.Ordinal);
        var currentEntities = currentSchema.Tables.Select(t => t.EntityName).ToHashSet(StringComparer.Ordinal);

        foreach (var (orphanName, (filePath, json, model)) in orphans)
        {
            var matched = model.Tables
                .Where(t => currentQualified.Contains(t.QualifiedName) || currentEntities.Contains(t.EntityName))
                .Select(t => t.QualifiedName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            if (matched.Count > 0)
            {
                return (orphanName, filePath, json, matched);
            }
        }

        return null;
    }

    private static void DetectCrossRegistryTableMoves(
        List<(string RegistryName, string SnapshotPath, string? RenamedFromPath, SchemaDiffResult Diff)> planned)
    {
        var droppedByTable = new Dictionary<string, string>(StringComparer.Ordinal);
        var createdByTable = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (registryName, _, _, diff) in planned)
        {
            if (diff.Operations is null)
            {
                continue;
            }

            foreach (var op in diff.Operations)
            {
                if (op is DropTableOp dt)
                {
                    var key = string.IsNullOrWhiteSpace(dt.Schema) ? dt.TableName : $"{dt.Schema}.{dt.TableName}";
                    droppedByTable[key] = registryName;
                }
                else if (op is CreateTableOp ct)
                {
                    createdByTable[ct.Table.QualifiedName] = registryName;
                }
            }
        }

        foreach (var (table, fromReg) in droppedByTable)
        {
            if (createdByTable.TryGetValue(table, out var toReg) &&
                !string.Equals(fromReg, toReg, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Table '{table}' was dropped in '{fromReg}' and created in '{toReg}' in the same migration. " +
                    "Moving an entity between registries would drop and recreate the table (losing data). " +
                    "Move the table entry between the two snapshot JSON files first, then re-run.");
            }
        }
    }
}
