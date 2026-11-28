using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SharpPersistence.Migrator.Cli.Config;

internal sealed record ScannedProjectInfo(
    string RelativePath,
    string FullPath,
    bool IsMigratorRoot,
    bool HasEntityRegistry);

internal sealed record ConfigGenerationResult(
    MigratorCliConfig Config,
    string ConfigPath,
    IReadOnlyList<string> Warnings);

internal static partial class SolutionProjectScanner
{
    private static readonly HashSet<string> ExcludedDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "out", "publish", ".git", ".vs", ".idea", "node_modules", "_trash"
    };

    public static string FindSolutionRoot(string? startDirectory = null)
    {
        var start = Path.GetFullPath(startDirectory ?? Directory.GetCurrentDirectory());
        var current = start;

        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.EnumerateFiles(current, "*.slnx", SearchOption.TopDirectoryOnly).Any() ||
                Directory.EnumerateFiles(current, "*.sln", SearchOption.TopDirectoryOnly).Any())
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent is null || string.Equals(parent.FullName, current, StringComparison.Ordinal))
            {
                break;
            }

            current = parent.FullName;
        }

        return start;
    }

    public static IReadOnlyList<ScannedProjectInfo> ScanSolution(string solutionRoot)
    {
        var csprojPaths = DiscoverCsprojPaths(solutionRoot);
        var results = new List<ScannedProjectInfo>(csprojPaths.Count);

        foreach (var fullPath in csprojPaths)
        {
            if (!File.Exists(fullPath))
            {
                continue;
            }

            var classification = ClassifyProject(fullPath);
            if (classification.IsTestProject)
            {
                continue;
            }

            var relPath = MigratorCliConfig.NormalizeRelativePath(solutionRoot, fullPath);
            results.Add(new ScannedProjectInfo(
                relPath,
                fullPath,
                classification.IsMigratorRoot,
                classification.HasEntityRegistry));
        }

        return results
            .OrderBy(static r => r.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsEligibleStartupProject(string csprojFullPath)
    {
        if (!File.Exists(csprojFullPath))
        {
            return false;
        }

        var info = ClassifyProject(csprojFullPath);
        return !info.IsTestProject && info.IsMigratorRoot;
    }

    public static ConfigGenerationResult GenerateOrRefresh(
        string solutionRoot,
        MigratorCliConfig? existingConfig)
    {
        var warnings = new List<string>();
        var scanned = ScanSolution(solutionRoot);

        var migratorProjects = scanned
            .Where(static p => p.IsMigratorRoot)
            .Select(static p => p.RelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var migratorSet = new HashSet<string>(migratorProjects, StringComparer.OrdinalIgnoreCase);

        // Validate & preserve manual otherProjects
        var validatedOthers = new List<string>();
        var seenOthers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (existingConfig is not null)
        {
            foreach (var rawOther in existingConfig.OtherProjects)
            {
                if (string.IsNullOrWhiteSpace(rawOther))
                {
                    continue;
                }

                var fullOther = MigratorCliConfig.ResolveAbsolutePath(solutionRoot, rawOther);
                if (!File.Exists(fullOther))
                {
                    warnings.Add($"Removed missing path from otherProjects: '{rawOther}'.");
                    continue;
                }

                var normalized = MigratorCliConfig.NormalizeRelativePath(solutionRoot, fullOther);
                if (migratorSet.Contains(normalized))
                {
                    warnings.Add($"Removed duplicate path from otherProjects (already discovered): '{normalized}'.");
                    continue;
                }

                if (seenOthers.Add(normalized))
                {
                    validatedOthers.Add(normalized);
                }
            }
        }

        // Validate existing defaultProject (existence + migrator-root eligibility)
        string? defaultProject = null;
        if (!string.IsNullOrWhiteSpace(existingConfig?.DefaultProject))
        {
            var rawDefault = existingConfig.DefaultProject!;
            var fullDefault = MigratorCliConfig.ResolveAbsolutePath(solutionRoot, rawDefault);
            if (!File.Exists(fullDefault))
            {
                warnings.Add($"Cleared defaultProject '{rawDefault}' because the file no longer exists.");
            }
            else if (!IsEligibleStartupProject(fullDefault))
            {
                warnings.Add($"Cleared defaultProject '{rawDefault}' because it is no longer an eligible migrator project.");
            }
            else
            {
                defaultProject = MigratorCliConfig.NormalizeRelativePath(solutionRoot, fullDefault);
            }
        }

        // Auto-select defaultProject when exactly one qualified migrator project exists
        if (string.IsNullOrWhiteSpace(defaultProject) && migratorProjects.Count == 1)
        {
            defaultProject = migratorProjects[0];
        }

        var config = new MigratorCliConfig
        {
            DefaultProject = defaultProject,
            Projects = migratorProjects,
            OtherProjects = validatedOthers
        };

        var configPath = Path.Combine(solutionRoot, MigratorCliConfig.FileName);
        return new ConfigGenerationResult(config, configPath, warnings);
    }

    private static List<string> DiscoverCsprojPaths(string solutionRoot)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var slnx = Directory.EnumerateFiles(solutionRoot, "*.slnx", SearchOption.TopDirectoryOnly)
            .OrderBy(static f => f, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (slnx is not null)
        {
            ParseSlnx(slnx, solutionRoot, paths);
        }
        else
        {
            var sln = Directory.EnumerateFiles(solutionRoot, "*.sln", SearchOption.TopDirectoryOnly)
                .OrderBy(static f => f, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (sln is not null)
            {
                ParseSln(sln, solutionRoot, paths);
            }
        }

        if (paths.Count == 0)
        {
            EnumerateCsprojRecursive(solutionRoot, paths);
        }

        return [.. paths];
    }

    private static void ParseSlnx(string slnxPath, string solutionRoot, HashSet<string> results)
    {
        try
        {
            var doc = XDocument.Load(slnxPath);
            foreach (var proj in doc.Descendants("Project"))
            {
                var relPath = proj.Attribute("Path")?.Value;
                if (string.IsNullOrWhiteSpace(relPath) ||
                    !relPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var full = Path.GetFullPath(Path.Combine(solutionRoot, relPath.Replace('/', Path.DirectorySeparatorChar)));
                results.Add(full);
            }
        }
        catch
        {
            // Fallback to recursive .csproj scan if .slnx cannot be parsed
        }
    }

    private static void ParseSln(string slnPath, string solutionRoot, HashSet<string> results)
    {
        try
        {
            var text = File.ReadAllText(slnPath);
            foreach (Match m in SlnProjectRegex().Matches(text))
            {
                var relPath = m.Groups[1].Value;
                var full = Path.GetFullPath(Path.Combine(solutionRoot, relPath.Replace('\\', Path.DirectorySeparatorChar)));
                results.Add(full);
            }
        }
        catch
        {
            // Fallback to recursive .csproj scan if .sln cannot be parsed
        }
    }

    private static void EnumerateCsprojRecursive(string dir, HashSet<string> results)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly))
        {
            results.Add(Path.GetFullPath(file));
        }

        foreach (var subDir in Directory.EnumerateDirectories(dir))
        {
            var name = Path.GetFileName(subDir);
            if (ExcludedDirNames.Contains(name))
            {
                continue;
            }

            EnumerateCsprojRecursive(subDir, results);
        }
    }

    private static (bool IsTestProject, bool IsMigratorRoot, bool HasEntityRegistry) ClassifyProject(string csprojPath)
    {
        var fileName = Path.GetFileName(csprojPath);
        var isTest = fileName.EndsWith(".Tests.csproj", StringComparison.OrdinalIgnoreCase) ||
                     fileName.EndsWith(".Test.csproj", StringComparison.OrdinalIgnoreCase);

        var isMigratorRoot = false;
        var hasEntityRegistry = false;

        try
        {
            var doc = XDocument.Load(csprojPath);
            if (!isTest)
            {
                var isTestProp = doc.Descendants()
                    .FirstOrDefault(static e => e.Name.LocalName.Equals("IsTestProject", StringComparison.OrdinalIgnoreCase))
                    ?.Value;
                if (string.Equals(isTestProp?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
                {
                    isTest = true;
                }

                if (!isTest && doc.Descendants()
                    .Where(static e => e.Name.LocalName.Equals("PackageReference", StringComparison.OrdinalIgnoreCase))
                    .Select(static e => e.Attribute("Include")?.Value ?? string.Empty)
                    .Any(static pkg =>
                        pkg.StartsWith("xunit", StringComparison.OrdinalIgnoreCase) ||
                        pkg.Equals("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase) ||
                        pkg.StartsWith("NUnit", StringComparison.OrdinalIgnoreCase) ||
                        pkg.StartsWith("MSTest", StringComparison.OrdinalIgnoreCase)))
                {
                    isTest = true;
                }
            }

            var isToolOrAnalyzer = doc.Descendants()
                .Any(static e =>
                    (e.Name.LocalName.Equals("PackAsTool", StringComparison.OrdinalIgnoreCase) ||
                     e.Name.LocalName.Equals("IsRoslynComponent", StringComparison.OrdinalIgnoreCase)) &&
                    string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));

            if (isTest || isToolOrAnalyzer)
            {
                return (true, false, false);
            }

            var rootProp = doc.Descendants()
                .FirstOrDefault(static e => e.Name.LocalName.Equals("EntityRegistryRoot", StringComparison.OrdinalIgnoreCase))
                ?.Value;
            if (string.Equals(rootProp?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
            {
                isMigratorRoot = true;
            }
        }
        catch
        {
            // Continue with source scan if XML parsing fails
        }

        var projectDir = Path.GetDirectoryName(csprojPath);
        if (!string.IsNullOrWhiteSpace(projectDir) && Directory.Exists(projectDir))
        {
            ScanProjectSourceFiles(projectDir, ref isMigratorRoot, ref hasEntityRegistry);
        }

        return (false, isMigratorRoot, hasEntityRegistry);
    }

    private static void ScanProjectSourceFiles(string dir, ref bool isMigratorRoot, ref bool hasEntityRegistry)
    {
        foreach (var csFile in Directory.EnumerateFiles(dir, "*.cs", SearchOption.TopDirectoryOnly))
        {
            if (isMigratorRoot && hasEntityRegistry)
            {
                return;
            }

            try
            {
                foreach (var rawLine in File.ReadLines(csFile))
                {
                    if (isMigratorRoot && hasEntityRegistry)
                    {
                        return;
                    }

                    var line = StripLineComment(rawLine);
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    if (!isMigratorRoot &&
                        (line.Contains("[assembly: EntityRegistryRoot", StringComparison.Ordinal) ||
                         line.Contains(".AddDbMigrator(", StringComparison.Ordinal) ||
                         line.Contains(": IDesignTimeDbMigratorFactory", StringComparison.Ordinal) ||
                         line.Contains(", IDesignTimeDbMigratorFactory", StringComparison.Ordinal)))
                    {
                        isMigratorRoot = true;
                    }

                    if (!hasEntityRegistry &&
                        (line.Contains("[EntityRegistry", StringComparison.Ordinal) ||
                         line.Contains(": IEntityRegistry", StringComparison.Ordinal) ||
                         line.Contains(", IEntityRegistry", StringComparison.Ordinal)))
                    {
                        hasEntityRegistry = true;
                    }
                }
            }
            catch
            {
                // Ignore unreadable files
            }
        }

        foreach (var subDir in Directory.EnumerateDirectories(dir))
        {
            if (isMigratorRoot && hasEntityRegistry)
            {
                return;
            }

            var name = Path.GetFileName(subDir);
            if (ExcludedDirNames.Contains(name))
            {
                continue;
            }

            ScanProjectSourceFiles(subDir, ref isMigratorRoot, ref hasEntityRegistry);
        }
    }

    private static string StripLineComment(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var commentIdx = trimmed.IndexOf("//", StringComparison.Ordinal);
        return commentIdx >= 0 ? trimmed[..commentIdx].TrimEnd() : trimmed;
    }

    [GeneratedRegex(@"Project\(""\{[^}]+\}""\)\s*=\s*""[^""]+"",\s*""([^""]+\.csproj)""", RegexOptions.IgnoreCase)]
    private static partial Regex SlnProjectRegex();
}
