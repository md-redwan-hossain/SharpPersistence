using System.Security.Cryptography;
using System.Text;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal sealed class MigrationScriptStore
{
    private readonly DbMigratorOptions _options;

    public MigrationScriptStore(DbMigratorOptions options)
    {
        _options = options;
    }

    public string ResolveMigrationsDirectory() => ResolvePath(_options.MigrationsDirectory);

    public string ResolveSnapshotDirectory() => ResolvePath(_options.SnapshotDirectory);

    public List<(Guid ScriptId, string RegistryName, string ScriptName, string FullPath)> DiscoverScripts()
    {
        var runlist = new MigrationRunlistManager(_options, this);
        return runlist.ResolveEffectiveOrder()
            .Select(e => (e.Id, e.RegistryName, e.ScriptName, e.FullPath))
            .ToList();
    }

    public async Task<IReadOnlyList<MigrationStatusItem>> BuildStatusItemsAsync(
        IReadOnlyList<AppliedMigrationRecord> applied,
        CancellationToken ct)
    {
        var appliedMap = applied.ToDictionary(a => a.ScriptId, a => a);

        var discovered = DiscoverScripts();
        var items = new List<MigrationStatusItem>(discovered.Count);

        foreach (var (scriptId, registryName, scriptName, fullPath) in discovered)
        {
            var content = await File.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);
            var checksum = ComputeSha256(content);

            if (appliedMap.TryGetValue(scriptId, out var record))
            {
                var mismatch = !string.Equals(record.ChecksumSha256, checksum, StringComparison.OrdinalIgnoreCase);
                items.Add(new MigrationStatusItem(scriptId, registryName, scriptName, fullPath, true, mismatch, record.AppliedAt));
            }
            else
            {
                items.Add(new MigrationStatusItem(scriptId, registryName, scriptName, fullPath, false, false, null));
            }
        }

        return items;
    }

    public static string ComputeSha256(string content)
    {
        var body = MigrationScriptHeader.StripHeader(content);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexStringLower(bytes);
    }

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        if (!string.IsNullOrWhiteSpace(_options.RootPath))
        {
            return Path.Combine(_options.RootPath, path);
        }

        if (!Directory.Exists(path))
        {
            var baseDirPath = Path.Combine(AppContext.BaseDirectory, path);
            if (Directory.Exists(baseDirPath))
            {
                return baseDirPath;
            }
        }

        return path;
    }
}
