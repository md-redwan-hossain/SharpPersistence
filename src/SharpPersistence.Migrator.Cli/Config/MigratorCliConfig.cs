using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpPersistence.Migrator.Cli.Config;

internal sealed class MigratorCliConfig
{
    public const string FileName = "db-migrator-config.json";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public string? DefaultStartupProject { get; set; }
    public string? DefaultDestinationProject { get; set; }
    public List<string> StartupProjects { get; set; } = [];
    public List<string> Projects { get; set; } = [];
    public List<string> OtherProjects { get; set; } = [];

    public static string? FindConfigPath(string? startDirectory = null)
    {
        var current = Path.GetFullPath(startDirectory ?? Directory.GetCurrentDirectory());
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(current);
            if (parent is null || string.Equals(parent.FullName, current, StringComparison.Ordinal))
            {
                break;
            }

            current = parent.FullName;
        }

        return null;
    }

    public static MigratorCliConfig Load(string configPath)
    {
        var json = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<MigratorCliConfig>(json, ReadOptions)
            ?? throw new InvalidOperationException($"Invalid '{FileName}' at '{configPath}'.");

        config.StartupProjects ??= [];
        config.Projects ??= [];
        config.OtherProjects ??= [];
        return config;
    }

    public void Save(string configPath)
    {
        var dir = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(this, WriteOptions);
        File.WriteAllText(configPath, json + Environment.NewLine);
    }

    public static string NormalizeRelativePath(string rootDir, string fullPath) =>
        Path.GetRelativePath(rootDir, Path.GetFullPath(fullPath)).Replace('\\', '/');

    public static string ResolveAbsolutePath(string configDir, string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(configDir, path.Replace('/', Path.DirectorySeparatorChar)));
    }
}
