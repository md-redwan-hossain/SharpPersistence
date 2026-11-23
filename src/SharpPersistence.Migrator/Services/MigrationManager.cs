using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Diffing;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

public sealed class MigrationManager : IMigrationManager
{
    private static readonly Lazy<DbProviderFactory?> CachedNpgsqlFactory = new(() =>
        ResolveProviderFactory("Npgsql.NpgsqlFactory", "Npgsql"));

    private static readonly Lazy<DbProviderFactory?> CachedOracleFactory = new(() =>
        ResolveProviderFactory("Oracle.ManagedDataAccess.Client.OracleClientFactory", "Oracle.ManagedDataAccess"));

    private static DbProviderFactory? ResolveProviderFactory(string typeName, string assemblyName)
    {
        var type = Type.GetType($"{typeName}, {assemblyName}");
        if (type is null)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
            type = asm?.GetType(typeName);
        }

        var prop = type?.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        return prop?.GetValue(null) as DbProviderFactory;
    }

    private readonly MigrationManagerOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly MigrationLockService _lockService;
    private readonly MigrationHistoryStore _historyStore;
    private readonly SchemaDriftVerifier _driftVerifier;

    public MigrationManager(MigrationManagerOptions options, IServiceProvider serviceProvider)
    {
        _options = options;
        _serviceProvider = serviceProvider;
        _lockService = new MigrationLockService(options);
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
        var stateDir = ResolveSnapshotDirectory();
        var sqlRootDir = ResolveMigrationsDirectory();
        var deferredDir = Path.Combine(sqlRootDir, "_deferred");

        if (!dryRun)
        {
            Directory.CreateDirectory(stateDir);
            Directory.CreateDirectory(sqlRootDir);
        }

        var sortedRegistries = RegistryDependencyResolver.SortTopologically(
            _options.Registries,
            _options.Dialect,
            _options.IdentifierCase,
            _options.PropertyConventions);

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

        if (_options.Dialect == SqlDialect.PostgreSql && string.IsNullOrWhiteSpace(registryFilter))
        {
            var globalSnapshotPath = Path.Combine(stateDir, "_global.json");
            var prevGlobalJson = File.Exists(globalSnapshotPath)
                ? await File.ReadAllTextAsync(globalSnapshotPath, ct)
                : null;

            var globalSchema = new DatabaseSchema
            {
                Extensions = _options.Extensions.Count > 0 ? [.. _options.Extensions] : null,
                Enums = _options.Enums.Count > 0 ? [.. _options.Enums] : null
            };

            if (prevGlobalJson is not null ||
                globalSchema.Extensions is { Count: > 0 } ||
                globalSchema.Enums is { Count: > 0 })
            {
                var globalDiff = StandaloneSchemaDiffer.GenerateDiff(
                    globalSchema,
                    prevGlobalJson,
                    _options.Dialect,
                    interactiveRenamePrompt: false);

                if (globalDiff.HasChanges)
                {
                    string? globalSqlPath = null;
                    if (!string.IsNullOrWhiteSpace(globalDiff.SqlScript))
                    {
                        var globalDir = Path.Combine(sqlRootDir, "_global");
                        globalSqlPath = Path.Combine(globalDir, $"{timestamp}_{migrationName}.sql");
                        if (!dryRun)
                        {
                            Directory.CreateDirectory(globalDir);
                            await File.WriteAllTextAsync(globalSqlPath, globalDiff.SqlScript, ct);
                        }
                    }

                    if (!dryRun)
                    {
                        await File.WriteAllTextAsync(globalSnapshotPath, globalDiff.UpdatedSnapshotJson, ct);
                    }

                    generated.Add(new GeneratedMigrationItem(
                        "_global",
                        globalSqlPath,
                        null,
                        globalDiff.SqlScript,
                        string.Empty,
                        false));
                }
            }
        }

        foreach (var registry in targets)
        {
            var registryName = registry.GetType().Name;
            var snapshotPath = Path.Combine(stateDir, $"{registryName}.json");
            var previousJson = File.Exists(snapshotPath)
                ? await File.ReadAllTextAsync(snapshotPath, ct)
                : null;

            var currentSchema = SchemaScanner.ScanRegistry(
                registry,
                _options.Dialect,
                _options.IdentifierCase,
                sortedRegistries,
                _options.PropertyConventions,
                _options.Enums);

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
                var emptySql =
                    $"-- Empty migration: {migrationName}{Environment.NewLine}BEGIN;{Environment.NewLine}{Environment.NewLine}-- Add custom SQL here{Environment.NewLine}{Environment.NewLine}COMMIT;{Environment.NewLine}";

                if (!dryRun)
                {
                    Directory.CreateDirectory(emptyDir);
                    await File.WriteAllTextAsync(emptySqlPath, emptySql, ct);
                }

                generated.Add(new GeneratedMigrationItem(registryName, emptySqlPath, null, emptySql, string.Empty, true));
                continue;
            }

            string? mainPath = null;
            string? deferredPath = null;

            if (!string.IsNullOrWhiteSpace(result.SqlScript))
            {
                var registrySqlDir = Path.Combine(sqlRootDir, registryName);
                mainPath = Path.Combine(registrySqlDir, $"{timestamp}_{migrationName}.sql");
                if (!dryRun)
                {
                    Directory.CreateDirectory(registrySqlDir);
                    await File.WriteAllTextAsync(mainPath, result.SqlScript, ct);
                }
            }

            if (!string.IsNullOrWhiteSpace(result.DeferredSqlScript))
            {
                deferredPath = Path.Combine(deferredDir, $"{timestamp}_{registryName}_{migrationName}.sql");
                if (!dryRun)
                {
                    Directory.CreateDirectory(deferredDir);
                    await File.WriteAllTextAsync(deferredPath, result.DeferredSqlScript, ct);
                }
            }

            if (!dryRun)
            {
                await File.WriteAllTextAsync(snapshotPath, result.UpdatedSnapshotJson, ct);
            }

            generated.Add(new GeneratedMigrationItem(
                registryName,
                mainPath,
                deferredPath,
                result.SqlScript,
                result.DeferredSqlScript,
                false));
        }

        return generated;
    }

    public async Task EnsureHistoryTableAsync(CancellationToken ct = default)
    {
        var (conn, ownsConnection) = await OpenConnectionAsync(ct);
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
        var (conn, ownsConnection) = await OpenConnectionAsync(ct);
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
        var appliedMap = applied.ToDictionary(
            a => (a.RegistryName, a.ScriptName),
            a => a);

        var discovered = DiscoverMigrationScripts();
        var items = new List<MigrationStatusItem>(discovered.Count);

        foreach (var (registryName, scriptName, fullPath) in discovered)
        {
            var content = await File.ReadAllTextAsync(fullPath, ct);
            var checksum = ComputeSha256(content);

            if (appliedMap.TryGetValue((registryName, scriptName), out var record))
            {
                var mismatch = !string.Equals(record.ChecksumSha256, checksum, StringComparison.OrdinalIgnoreCase);
                items.Add(new MigrationStatusItem(registryName, scriptName, fullPath, true, mismatch, record.AppliedAt));
            }
            else
            {
                items.Add(new MigrationStatusItem(registryName, scriptName, fullPath, false, false, null));
            }
        }

        return items;
    }

    public async Task<IReadOnlyList<string>> MigrateAsync(bool dryRun = false, CancellationToken ct = default)
    {
        if (dryRun)
        {
            return await GetPendingMigrationsAsync(ct);
        }

        var (conn, ownsConnection) = await OpenConnectionAsync(ct);
        IAsyncDisposable? migrationLock = null;

        try
        {
            migrationLock = await _lockService.AcquireAsync(conn, ct);
            await _historyStore.EnsureHistoryTableAsync(conn, ct);

            var applied = await _historyStore.ReadAppliedAsync(conn, ct);
            var appliedSet = applied.Select(a => (a.RegistryName, a.ScriptName)).ToHashSet();

            var scripts = DiscoverMigrationScripts();
            var executed = new List<string>();

            foreach (var (registryName, scriptName, fullPath) in scripts)
            {
                if (appliedSet.Contains((registryName, scriptName)))
                {
                    continue;
                }

                var sql = await File.ReadAllTextAsync(fullPath, ct);
                var checksum = ComputeSha256(sql);

                if (_options.Dialect == SqlDialect.Oracle)
                {
                    foreach (var stmt in SplitOracleStatements(sql))
                    {
                        await using var execCmd = conn.CreateCommand();
                        execCmd.CommandText = stmt;
                        await execCmd.ExecuteNonQueryAsync(ct);
                    }
                }
                else
                {
                    await using var execCmd = conn.CreateCommand();
                    execCmd.CommandText = sql;
                    await execCmd.ExecuteNonQueryAsync(ct);
                }

                await _historyStore.RecordAppliedAsync(conn, registryName, scriptName, checksum, ct);
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
        var translatedDir = Path.Combine(ResolveMigrationsDirectory(), "_translated");
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

        var (conn, ownsConnection) = await OpenConnectionAsync(ct);
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
                    var checksum = ComputeSha256(sql);
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

    public async Task<IReadOnlyList<SchemaDriftItem>> VerifySchemaAsync(CancellationToken ct = default)
    {
        var (conn, ownsConnection) = await OpenConnectionAsync(ct);
        try
        {
            return await _driftVerifier.VerifyAsync(conn, ct);
        }
        finally
        {
            if (ownsConnection)
            {
                await conn.DisposeAsync();
            }
        }
    }

    internal string ResolveMigrationsDirectory() =>
        ResolvePath(_options.MigrationsDirectory);

    private List<(string RegistryName, string ScriptName, string FullPath)> DiscoverMigrationScripts()
    {
        var sortedRegistries = RegistryDependencyResolver.SortTopologically(
            _options.Registries,
            _options.Dialect,
            _options.IdentifierCase,
            _options.PropertyConventions);

        var folders = new List<string> { "_global", "_translated" };
        folders.AddRange(sortedRegistries.Select(r => r.GetType().Name));
        folders.Add("_deferred");

        var results = new List<(string, string, string)>();
        var migrationsRoot = ResolveMigrationsDirectory();
        foreach (var folder in folders)
        {
            var dir = Path.Combine(migrationsRoot, folder);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            var files = Directory.GetFiles(dir, "*.sql").OrderBy(Path.GetFileName, StringComparer.Ordinal);
            foreach (var file in files)
            {
                results.Add((folder, Path.GetFileName(file), file));
            }
        }

        return results;
    }

    private async Task<(DbConnection Connection, bool OwnsConnection)> OpenConnectionAsync(CancellationToken ct)
    {
        DbConnection conn;
        bool ownsConnection;
        if (_options.ConnectionFactory is not null)
        {
            conn = _options.ConnectionFactory(_serviceProvider);
            ownsConnection = false;
        }
        else if (!string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            var factory = _options.ProviderFactory
                          ?? _options.Dialect switch
                          {
                              SqlDialect.PostgreSql => CachedNpgsqlFactory.Value,
                              SqlDialect.Oracle => CachedOracleFactory.Value,
                              _ => null
                          }
                          ?? throw new InvalidOperationException(
                              $"No DbProviderFactory configured and default factory for '{_options.Dialect}' could not be loaded. " +
                              "Install the provider package (e.g. Npgsql or Oracle.ManagedDataAccess.Core) or pass DbProviderFactory / connectionFactory.");

            conn = factory.CreateConnection()
                   ?? throw new InvalidOperationException("DbProviderFactory.CreateConnection() returned null.");
            conn.ConnectionString = _options.ConnectionString;
            ownsConnection = true;
        }
        else
        {
            throw new InvalidOperationException(
                "No database connection configured. Pass a connection string or connectionFactory inside AddDbMigrator.");
        }

        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        return (conn, ownsConnection);
    }

    internal static IEnumerable<string> SplitOracleStatements(string sql)
    {
        var lines = sql.Replace("\r\n", "\n").Split('\n');
        var current = new StringBuilder();
        var inPlSqlBlock = false;

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            if (trimmed.StartsWith("--", StringComparison.Ordinal) || string.IsNullOrEmpty(trimmed))
            {
                if (inPlSqlBlock)
                {
                    current.AppendLine(rawLine);
                }
                continue;
            }

            if (!inPlSqlBlock && current.Length == 0 &&
                (trimmed.StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.StartsWith("DECLARE", StringComparison.OrdinalIgnoreCase)))
            {
                inPlSqlBlock = true;
            }

            if (inPlSqlBlock)
            {
                if (trimmed == "/")
                {
                    var block = current.ToString().Trim();
                    current.Clear();
                    inPlSqlBlock = false;
                    if (!string.IsNullOrWhiteSpace(block))
                    {
                        yield return block;
                    }
                }
                else
                {
                    current.AppendLine(rawLine);
                }
            }
            else
            {
                current.AppendLine(rawLine);
                if (trimmed.EndsWith(';'))
                {
                    var stmt = current.ToString().Trim().TrimEnd(';').Trim();
                    current.Clear();
                    if (!string.IsNullOrWhiteSpace(stmt))
                    {
                        yield return stmt;
                    }
                }
            }
        }

        var tail = current.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(tail))
        {
            yield return inPlSqlBlock ? tail : tail.TrimEnd(';').Trim();
        }
    }

    private string ResolveSnapshotDirectory() =>
        ResolvePath(_options.SnapshotDirectory);

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path) || string.IsNullOrWhiteSpace(_options.RootPath))
        {
            return path;
        }

        return Path.Combine(_options.RootPath, path);
    }

    private static string ComputeSha256(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(bytes);
    }
}
