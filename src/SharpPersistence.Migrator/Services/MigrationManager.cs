using System.Globalization;
using System.Reflection;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Diffing;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal sealed class MigrationManager : IMigrationManager
{
    private readonly MigrationManagerOptions _options;
    private readonly IMigrationDialect _dialect;
    private readonly MigrationConnectionOpener _connectionOpener;
    private readonly MigrationScriptStore _scriptStore;
    private readonly MigrationHistoryStore _historyStore;
    private readonly SchemaDriftVerifier _driftVerifier;

    public MigrationManager(MigrationManagerOptions options, IServiceProvider serviceProvider)
    {
        _options = options;
        _dialect = MigrationDialectRegistry.Get(options);
        _connectionOpener = new MigrationConnectionOpener(options, serviceProvider, _dialect);
        _scriptStore = new MigrationScriptStore(options);
        _historyStore = new MigrationHistoryStore(options);
        _driftVerifier = new SchemaDriftVerifier(options);
    }

    internal MigrationManagerOptions Options => _options;

    public async Task<IReadOnlyList<GeneratedMigrationItem>> GenerateAsync(
        string migrationName,
        string? registryFilter = null,
        bool allowEmpty = false,
        bool interactiveRenamePrompt = true,
        bool dryRun = false,
        Func<string, bool>? confirmRename = null,
        CancellationToken ct = default)
    {
        var stateDir = _scriptStore.ResolveSnapshotDirectory();
        var sqlRootDir = _scriptStore.ResolveMigrationsDirectory();
        var deferredDir = Path.Combine(sqlRootDir, "_deferred");

        if (!dryRun)
        {
            CreateRunlistManager().EnsureReadyForAppend();
            Directory.CreateDirectory(stateDir);
            Directory.CreateDirectory(sqlRootDir);
        }

        var sortedRegistries = RegistryDependencyResolver.SortTopologically(_options);

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

        if (_dialect.SupportsGlobalExtensionsAndEnums && string.IsNullOrWhiteSpace(registryFilter))
        {
            var globalItem = await GenerateGlobalObjectsAsync(migrationName, timestamp, stateDir, sqlRootDir, dryRun, ct);
            if (globalItem is not null)
            {
                generated.Add(globalItem.Item);
                generatedScriptIds.Add(globalItem.ScriptId);
            }
        }

        foreach (var registry in targets)
        {
            var registryName = registry.GetType().Name;
            var snapshotPath = Path.Combine(stateDir, $"{registryName}.json");
            var previousJson = File.Exists(snapshotPath)
                ? await File.ReadAllTextAsync(snapshotPath, ct)
                : null;

            var currentSchema = SchemaScanner.ScanRegistry(registry, _options, sortedRegistries);

            var result = StandaloneSchemaDiffer.GenerateDiff(
                currentSchema,
                previousJson,
                _options.Dialect,
                interactiveRenamePrompt,
                _options.OracleCompatibility,
                confirmRename);

            if (!result.HasChanges)
            {
                if (!allowEmpty)
                {
                    continue;
                }

                var emptyDir = Path.Combine(sqlRootDir, registryName);
                var emptySqlPath = Path.Combine(emptyDir, $"{timestamp}_{migrationName}.sql");
                var (emptySql, emptyId, _) = MigrationScriptHeader.StampHeader(_dialect.FormatEmptyMigration(migrationName));

                if (!dryRun)
                {
                    Directory.CreateDirectory(emptyDir);
                    await File.WriteAllTextAsync(emptySqlPath, emptySql, ct);
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
                (mainSql, var mainId, _) = MigrationScriptHeader.StampHeader(mainSql);
                var registrySqlDir = Path.Combine(sqlRootDir, registryName);
                mainPath = Path.Combine(registrySqlDir, $"{timestamp}_{migrationName}.sql");
                if (!dryRun)
                {
                    Directory.CreateDirectory(registrySqlDir);
                    await File.WriteAllTextAsync(mainPath, mainSql, ct);
                }

                generatedScriptIds.Add(mainId);
            }

            if (!string.IsNullOrWhiteSpace(deferredSql))
            {
                (deferredSql, var deferredId, _) = MigrationScriptHeader.StampHeader(deferredSql);
                deferredPath = Path.Combine(deferredDir, $"{timestamp}_{registryName}_{migrationName}.sql");
                if (!dryRun)
                {
                    Directory.CreateDirectory(deferredDir);
                    await File.WriteAllTextAsync(deferredPath, deferredSql, ct);
                }

                deferredScriptIds.Add(deferredId);
            }

            if (!dryRun)
            {
                await File.WriteAllTextAsync(snapshotPath, result.UpdatedSnapshotJson, ct);
            }

            generated.Add(new GeneratedMigrationItem(
                registryName,
                mainPath,
                deferredPath,
                mainSql,
                deferredSql,
                false));
        }

        if (!dryRun && (generatedScriptIds.Count > 0 || deferredScriptIds.Count > 0))
        {
            var runlist = new MigrationRunlistManager(_options, _scriptStore);
            var allNewIds = new List<Guid>(generatedScriptIds.Count + deferredScriptIds.Count);
            allNewIds.AddRange(generatedScriptIds);
            allNewIds.AddRange(deferredScriptIds);
            await runlist.AppendNewScriptsAsync(allNewIds, ct);
        }

        return generated;
    }

    public async Task EnsureHistoryTableAsync(CancellationToken ct = default)
    {
        var (conn, ownsConnection) = await _connectionOpener.OpenAsync(ct);
        try
        {
            await _historyStore.EnsureHistoryTableAsync(conn, ct);
        }
        finally
        {
            if (ownsConnection)
            {
                await conn.DisposeAsync();
            }
        }
    }

    public async Task<IReadOnlyList<AppliedMigrationRecord>> GetAppliedMigrationsAsync(CancellationToken ct = default)
    {
        var (conn, ownsConnection) = await _connectionOpener.OpenAsync(ct);
        try
        {
            await _historyStore.EnsureHistoryTableAsync(conn, ct);
            return await _historyStore.ReadAppliedAsync(conn, ct);
        }
        finally
        {
            if (ownsConnection)
            {
                await conn.DisposeAsync();
            }
        }
    }

    public async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken ct = default)
    {
        var status = await GetStatusAsync(ct);
        return status.Where(s => !s.IsApplied).Select(s => $"{s.RegistryName}/{s.ScriptName}").ToList();
    }

    public async Task<IReadOnlyList<MigrationStatusItem>> GetStatusAsync(CancellationToken ct = default)
    {
        var applied = await GetAppliedMigrationsAsync(ct);
        return await _scriptStore.BuildStatusItemsAsync(applied, ct);
    }

    public async Task<IReadOnlyList<string>> MigrateAsync(bool dryRun = false, CancellationToken ct = default)
    {
        if (dryRun)
        {
            return await GetPendingMigrationsAsync(ct);
        }

        var (conn, ownsConnection) = await _connectionOpener.OpenAsync(ct);
        IAsyncDisposable? migrationLock = null;

        try
        {
            migrationLock = await _dialect.AcquireLockAsync(conn, _options, ct);
            await _historyStore.EnsureHistoryTableAsync(conn, ct);

            var applied = await _historyStore.ReadAppliedAsync(conn, ct);
            var appliedMap = applied.ToDictionary(a => (a.RegistryName, a.ScriptName));

            var scripts = _scriptStore.DiscoverScripts();
            var checksumMismatches = new List<string>();
            foreach (var (registryName, scriptName, fullPath) in scripts)
            {
                if (appliedMap.TryGetValue((registryName, scriptName), out var record))
                {
                    var existingSql = await File.ReadAllTextAsync(fullPath, ct);
                    var currentChecksum = MigrationScriptStore.ComputeSha256(existingSql);
                    if (!string.Equals(record.ChecksumSha256, currentChecksum, StringComparison.OrdinalIgnoreCase))
                    {
                        checksumMismatches.Add($"{registryName}/{scriptName}");
                    }
                }
            }

            if (checksumMismatches.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Refusing to apply migrations because {checksumMismatches.Count} already-applied migration script(s) were modified on disk: " +
                    $"{string.Join(", ", checksumMismatches)}. Create a new migration script instead of editing applied ones.");
            }

            var executed = new List<string>();

            foreach (var (registryName, scriptName, fullPath) in scripts)
            {
                if (appliedMap.ContainsKey((registryName, scriptName)))
                {
                    continue;
                }

                var sql = await File.ReadAllTextAsync(fullPath, ct);
                var checksum = MigrationScriptStore.ComputeSha256(sql);

                if (_dialect.TryStripOuterTransaction(sql, out var innerSql))
                {
                    await using var tx = await conn.BeginTransactionAsync(ct);
                    try
                    {
                        foreach (var stmt in _dialect.SplitScriptStatements(innerSql))
                        {
                            if (string.IsNullOrWhiteSpace(stmt))
                            {
                                continue;
                            }

                            await using var execCmd = conn.CreateCommand();
                            execCmd.Transaction = tx;
                            execCmd.CommandText = stmt;
                            await execCmd.ExecuteNonQueryAsync(ct);
                        }

                        await _historyStore.RecordAppliedAsync(conn, registryName, scriptName, checksum, ct, tx);
                        await tx.CommitAsync(ct);
                    }
                    catch
                    {
                        await tx.RollbackAsync(CancellationToken.None);
                        throw;
                    }
                }
                else
                {
                    foreach (var stmt in _dialect.SplitScriptStatements(sql))
                    {
                        if (string.IsNullOrWhiteSpace(stmt))
                        {
                            continue;
                        }

                        await using var execCmd = conn.CreateCommand();
                        execCmd.CommandText = stmt;
                        await execCmd.ExecuteNonQueryAsync(ct);
                    }

                    await _historyStore.RecordAppliedAsync(conn, registryName, scriptName, checksum, ct);
                }

                executed.Add($"{registryName}/{scriptName}");
            }

            return executed;
        }
        finally
        {
            if (migrationLock is not null)
            {
                await migrationLock.DisposeAsync();
            }

            if (ownsConnection)
            {
                await conn.DisposeAsync();
            }
        }
    }

    public async Task<IReadOnlyList<string>> SyncEfCoreHistoryAsync(
        bool dryRun = false,
        CancellationToken ct = default)
    {
        var translatedDir = Path.Combine(_scriptStore.ResolveMigrationsDirectory(), "_translated");
        if (!Directory.Exists(translatedDir))
        {
            return [];
        }

        var legacyScripts = Directory.GetFiles(translatedDir, "*.sql")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToDictionary(
                path => Path.GetFileNameWithoutExtension(path),
                path => (ScriptName: Path.GetFileName(path), FullPath: path),
                StringComparer.Ordinal);

        if (legacyScripts.Count == 0)
        {
            return [];
        }

        var (conn, ownsConnection) = await _connectionOpener.OpenAsync(ct);
        try
        {
            await _historyStore.EnsureHistoryTableAsync(conn, ct);
            var efAppliedIds = await _historyStore.ReadEfCoreMigrationIdsAsync(conn, ct);
            if (efAppliedIds.Count == 0)
            {
                return [];
            }

            var existingApplied = (await _historyStore.ReadAppliedAsync(conn, ct))
                .Select(a => (a.RegistryName, a.ScriptName))
                .ToHashSet();

            var synced = new List<string>();
            foreach (var migrationId in efAppliedIds)
            {
                if (!legacyScripts.TryGetValue(migrationId, out var match) ||
                    existingApplied.Contains(("_translated", match.ScriptName)))
                {
                    continue;
                }

                if (!dryRun)
                {
                    var sql = await File.ReadAllTextAsync(match.FullPath, ct);
                    var checksum = MigrationScriptStore.ComputeSha256(sql);
                    await _historyStore.RecordAppliedAsync(conn, "_translated", match.ScriptName, checksum, ct);
                }

                synced.Add($"_translated/{match.ScriptName}");
            }

            return synced;
        }
        finally
        {
            if (ownsConnection)
            {
                await conn.DisposeAsync();
            }
        }
    }

    public Task<DoctorReport> DiagnoseAsync(
        string? registryFilter = null,
        bool checkDatabase = false,
        IReadOnlyList<Assembly>? discoveredAssemblies = null,
        CancellationToken ct = default)
    {
        var doctor = new MigrationDoctor(
            _options,
            _dialect,
            _connectionOpener,
            _scriptStore,
            _historyStore,
            _driftVerifier);
        return doctor.DiagnoseAsync(registryFilter, checkDatabase, discoveredAssemblies, ct);
    }

    internal string ResolveMigrationsDirectory() => _scriptStore.ResolveMigrationsDirectory();

    internal MigrationRunlistManager CreateRunlistManager() => new(_options, _scriptStore);

    internal static IEnumerable<string> SplitOracleStatements(string sql) =>
        OracleMigrationDialect.SplitStatements(sql);

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
            ? await File.ReadAllTextAsync(globalSnapshotPath, ct)
            : null;

        var globalSchema = new DatabaseModel
        {
            Extensions = _options.Extensions.Count > 0 ? [.. _options.Extensions] : null,
            Enums = _options.Enums.Count > 0 ? [.. _options.Enums] : null
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
            _options.Dialect,
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
            (globalSql, globalId, _) = MigrationScriptHeader.StampHeader(globalSql);
            var globalDir = Path.Combine(sqlRootDir, "_global");
            globalSqlPath = Path.Combine(globalDir, $"{timestamp}_{migrationName}.sql");
            if (!dryRun)
            {
                Directory.CreateDirectory(globalDir);
                await File.WriteAllTextAsync(globalSqlPath, globalSql, ct);
            }
        }

        if (!dryRun)
        {
            await File.WriteAllTextAsync(globalSnapshotPath, globalDiff.UpdatedSnapshotJson, ct);
        }

        return (new GeneratedMigrationItem(
            "_global",
            globalSqlPath,
            null,
            globalSql,
            string.Empty,
            false), globalId);
    }
}
