using System.Security.Cryptography;
using System.Text;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal sealed class MigrationScriptStore
{
    private readonly MigrationManagerOptions _options;

    public MigrationScriptStore(MigrationManagerOptions options)
    {
        _options = options;
    }

    public string ResolveMigrationsDirectory() => ResolvePath(_options.MigrationsDirectory);

    public string ResolveSnapshotDirectory() => ResolvePath(_options.SnapshotDirectory);

    public List<(string RegistryName, string ScriptName, string FullPath)> DiscoverScripts()
    {
        var sortedRegistries = RegistryDependencyResolver.SortTopologically(
            _options.Registries,
            _options.Dialect,
            _options.IdentifierCase,
            _options.PropertyConventions,
            _options.PluralizeTableNames);

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

    public async Task<IReadOnlyList<MigrationStatusItem>> BuildStatusItemsAsync(
        IReadOnlyList<AppliedMigrationRecord> applied,
        CancellationToken ct)
    {
        var appliedMap = applied.ToDictionary(
            a => (a.RegistryName, a.ScriptName),
            a => a);

        var discovered = DiscoverScripts();
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

    public static string ComputeSha256(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(bytes);
    }

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path) || string.IsNullOrWhiteSpace(_options.RootPath))
        {
            return path;
        }

        return Path.Combine(_options.RootPath, path);
    }
}
