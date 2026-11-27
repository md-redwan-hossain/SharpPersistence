using System.Globalization;
using System.Reflection;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Diffing;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal sealed class DbMigrator : IDbMigrator
{
    private readonly DbMigratorOptions _options;
    private readonly IMigrationDialect _dialect;
    private readonly MigrationConnectionOpener _connectionOpener;
    private readonly MigrationScriptStore _scriptStore;
    private readonly MigrationHistoryStore _historyStore;
    private readonly SchemaDriftVerifier _driftVerifier;

    public DbMigrator(DbMigratorOptions options, IServiceProvider serviceProvider)
    {
        _options = options;
        _dialect = MigrationDialectRegistry.Get(options);
        _connectionOpener = new MigrationConnectionOpener(options, serviceProvider, _dialect);
        _scriptStore = new MigrationScriptStore(options);
        _historyStore = new MigrationHistoryStore(options);
        _driftVerifier = new SchemaDriftVerifier(options);
    }

    internal DbMigratorOptions Options => _options;
    internal MigrationConnectionOpener ConnectionOpener => _connectionOpener;

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

            var currentSchema = SchemaScanner.ScanRegistry(registry, _options, sortedRegistries);

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
                _options.Dialect,
                interactiveRenamePrompt,
                _options.OracleCompatibility,
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
                var emptySql = _dialect.FormatEmptyMigration(migrationName);

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
            var runlist = new MigrationRunlistManager(_options, _scriptStore);
            var allNewIds = new List<Guid>(generatedScriptIds.Count + deferredScriptIds.Count);
            allNewIds.AddRange(generatedScriptIds);
            allNewIds.AddRange(deferredScriptIds);
            await runlist.AppendNewScriptsAsync(allNewIds, ct).ConfigureAwait(false);
        }

        return generated;
    }

    public async Task EnsureHistoryTableAsync(CancellationToken cancellationToken = default)
    {
        var (conn, ownsConnection) = await _connectionOpener.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _historyStore.EnsureHistoryTableAsync(conn, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (ownsConnection)
            {
                await conn.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<IReadOnlyList<AppliedMigrationRecord>> GetAppliedMigrationsAsync(CancellationToken cancellationToken = default)
    {
        var (conn, ownsConnection) = await _connectionOpener.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _historyStore.EnsureHistoryTableAsync(conn, cancellationToken).ConfigureAwait(false);
            return await _historyStore.ReadAppliedAsync(conn, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (ownsConnection)
            {
                await conn.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<bool> CanConnectToDatabaseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var (conn, ownsConnection) = await _connectionOpener.OpenAsync(cancellationToken).ConfigureAwait(false);
            if (ownsConnection)
            {
                await conn.DisposeAsync().ConfigureAwait(false);
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> HasPendingMigrationAsync(CancellationToken cancellationToken = default)
    {
        var discovered = _scriptStore.DiscoverScripts();
        if (discovered.Count == 0)
        {
            return false;
        }

        var (conn, ownsConnection) = await _connectionOpener.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await _historyStore.HistoryTableExistsAsync(conn, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            var applied = await _historyStore.ReadAppliedAsync(conn, cancellationToken).ConfigureAwait(false);
            var appliedIds = applied.Select(static a => a.ScriptId).ToHashSet();
            return discovered.Any(s => !appliedIds.Contains(s.ScriptId));
        }
        finally
        {
            if (ownsConnection)
            {
                await conn.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken = default)
    {
        var discovered = _scriptStore.DiscoverScripts();
        if (discovered.Count == 0)
        {
            return [];
        }

        var (conn, ownsConnection) = await _connectionOpener.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await _historyStore.HistoryTableExistsAsync(conn, cancellationToken).ConfigureAwait(false))
            {
                return discovered.Select(s => $"{s.RegistryName}/{s.ScriptName}").ToList();
            }

            var applied = await _historyStore.ReadAppliedAsync(conn, cancellationToken).ConfigureAwait(false);
            var appliedIds = applied.Select(static a => a.ScriptId).ToHashSet();
            return discovered
                .Where(s => !appliedIds.Contains(s.ScriptId))
                .Select(s => $"{s.RegistryName}/{s.ScriptName}")
                .ToList();
        }
        finally
        {
            if (ownsConnection)
            {
                await conn.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<IReadOnlyList<MigrationStatusItem>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var applied = await GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false);
        return await _scriptStore.BuildStatusItemsAsync(applied, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> MigrateAsync(
        bool dryRun = false,
        bool skipPermissionCheck = false,
        CancellationToken cancellationToken = default)
    {
        if (dryRun)
        {
            return await GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);
        }

        var (conn, ownsConnection) = await _connectionOpener.OpenAsync(cancellationToken).ConfigureAwait(false);
        IAsyncDisposable? migrationLock = null;

        try
        {
            migrationLock = await _dialect.AcquireLockAsync(conn, _options, cancellationToken).ConfigureAwait(false);
            var historyExists = await _historyStore.HistoryTableExistsAsync(conn, cancellationToken).ConfigureAwait(false);
            var applied = historyExists
                ? await _historyStore.ReadAppliedAsync(conn, cancellationToken).ConfigureAwait(false)
                : [];
            var appliedMap = applied.ToDictionary(a => a.ScriptId);

            var scripts = _scriptStore.DiscoverScripts();
            var checksumMismatches = new List<string>();
            var pendingScripts = new List<(Guid ScriptId, string RelativeScriptName, string Sql)>();

            foreach (var (scriptId, registryName, scriptName, fullPath) in scripts)
            {
                var sql = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
                if (appliedMap.TryGetValue(scriptId, out var record))
                {
                    var currentChecksum = MigrationScriptStore.ComputeSha256(sql);
                    if (!string.Equals(record.ChecksumSha256, currentChecksum, StringComparison.OrdinalIgnoreCase))
                    {
                        checksumMismatches.Add($"{registryName}/{scriptName}");
                    }
                }
                else
                {
                    pendingScripts.Add((scriptId, $"{registryName}/{scriptName}", sql));
                }
            }

            if (checksumMismatches.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Refusing to apply migrations because {checksumMismatches.Count} already-applied migration script(s) were modified on disk: " +
                    $"{string.Join(", ", checksumMismatches)}. Create a new migration script instead of editing applied ones.");
            }

            if (!skipPermissionCheck && (pendingScripts.Count > 0 || !historyExists))
            {
                var requirements = SqlPermissionRequirementExtractor.Extract(
                    pendingScripts.Select(static p => p.Sql),
                    _options,
                    _dialect,
                    historyExists);
                var permissionChecks = await _dialect.CheckPermissionsAsync(conn, requirements, cancellationToken).ConfigureAwait(false);
                var permissionErrors = permissionChecks.Where(static c => c.Severity == DoctorSeverity.Error).ToList();
                if (permissionErrors.Count > 0)
                {
                    var details = string.Join(
                        Environment.NewLine,
                        permissionErrors.Select(static e =>
                            string.IsNullOrWhiteSpace(e.Hint)
                                ? $"  - {e.Message}"
                                : $"  - {e.Message}{Environment.NewLine}    Hint: {e.Hint}"));
                    throw new InvalidOperationException(
                        $"Migration aborted: database user is missing required permissions:{Environment.NewLine}{details}");
                }
            }

            if (!historyExists)
            {
                await _historyStore.EnsureHistoryTableAsync(conn, cancellationToken).ConfigureAwait(false);
            }

            var executed = new List<string>(pendingScripts.Count);

            foreach (var (scriptId, relativeScriptName, sql) in pendingScripts)
            {
                var checksum = MigrationScriptStore.ComputeSha256(sql);

                if (_dialect.TryStripOuterTransaction(sql, out var innerSql))
                {
                    await using var tx = await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
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
                            await execCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                        }

                        await _historyStore.RecordAppliedAsync(conn, scriptId, relativeScriptName, checksum, cancellationToken, tx).ConfigureAwait(false);
                        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
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
                        await execCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }

                    await _historyStore.RecordAppliedAsync(conn, scriptId, relativeScriptName, checksum, cancellationToken).ConfigureAwait(false);
                }

                executed.Add(relativeScriptName);
            }

            return executed;
        }
        finally
        {
            if (migrationLock is not null)
            {
                await migrationLock.DisposeAsync().ConfigureAwait(false);
            }

            if (ownsConnection)
            {
                await conn.DisposeAsync().ConfigureAwait(false);
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

        var legacyScripts = new Dictionary<string, (Guid ScriptId, string ScriptName, string FullPath)>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.GetFiles(translatedDir, "*.sql").OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var fileName = Path.GetFileName(path);
            var stem = Path.GetFileNameWithoutExtension(path);
            var hasId = MigrationScriptHeader.TryParseIdFromFileName(path, out var scriptId);
            if (!hasId)
            {
                continue;
            }

            var key = MigrationScriptHeader.StripIdPrefix(stem);
            legacyScripts[key] = (scriptId, fileName, path);
        }

        if (legacyScripts.Count == 0)
        {
            return [];
        }

        var (conn, ownsConnection) = await _connectionOpener.OpenAsync(ct).ConfigureAwait(false);
        try
        {
            await _historyStore.EnsureHistoryTableAsync(conn, ct).ConfigureAwait(false);
            var efAppliedIds = await _historyStore.ReadEfCoreMigrationIdsAsync(conn, ct).ConfigureAwait(false);
            if (efAppliedIds.Count == 0)
            {
                return [];
            }

            var existingApplied = (await _historyStore.ReadAppliedAsync(conn, ct).ConfigureAwait(false))
                .Select(a => a.ScriptId)
                .ToHashSet();

            var synced = new List<string>();
            foreach (var migrationId in efAppliedIds)
            {
                var normalizedId = MigrationScriptHeader.NormalizeScriptName(migrationId);
                if ((!legacyScripts.TryGetValue(migrationId, out var match) &&
                     !legacyScripts.TryGetValue(normalizedId, out match)) ||
                    existingApplied.Contains(match.ScriptId))
                {
                    continue;
                }

                var relativeScriptName = $"_translated/{match.ScriptName}";
                if (!dryRun)
                {
                    var sql = await File.ReadAllTextAsync(match.FullPath, ct).ConfigureAwait(false);
                    var checksum = MigrationScriptStore.ComputeSha256(sql);
                    await _historyStore.RecordAppliedAsync(conn, match.ScriptId, relativeScriptName, checksum, ct).ConfigureAwait(false);
                }

                synced.Add(relativeScriptName);
            }

            return synced;
        }
        finally
        {
            if (ownsConnection)
            {
                await conn.DisposeAsync().ConfigureAwait(false);
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
            ? await File.ReadAllTextAsync(globalSnapshotPath, ct).ConfigureAwait(false)
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
