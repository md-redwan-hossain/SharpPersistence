using System.Reflection;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
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

    public Task<IReadOnlyList<GeneratedMigrationItem>> GenerateAsync(
        string migrationName,
        string? registryFilter = null,
        bool allowEmpty = false,
        bool interactiveRenamePrompt = true,
        bool dryRun = false,
        Func<string, bool>? confirmRename = null,
        CancellationToken ct = default)
    {
        var generator = new MigrationScriptGenerator(_options, _dialect, _scriptStore);
        return generator.GenerateAsync(migrationName, registryFilter, allowEmpty, interactiveRenamePrompt, dryRun, confirmRename, ct);
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
}
