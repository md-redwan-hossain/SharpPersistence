namespace SharpPersistence.Migrator.Cli.Config;

internal sealed record ResolvedProjectPaths(
    string? StartupProjectPath,
    string? DestinationProjectPath);

internal static class ProjectResolutionService
{
    public static ResolvedProjectPaths Resolve(
        SharedSettings settings,
        string? startDirectory = null,
        Func<string, IReadOnlyList<ProjectChoice>, ProjectChoice>? picker = null)
    {
        var explicitStartup = !string.IsNullOrWhiteSpace(settings.StartupProjectPath);
        var explicitDestination = !string.IsNullOrWhiteSpace(settings.DestinationProjectPath);

        // If user explicitly passed --startup-project, honor flags directly (no config required)
        if (explicitStartup)
        {
            return new ResolvedProjectPaths(settings.StartupProjectPath, settings.DestinationProjectPath);
        }

        var configPath = MigratorCliConfig.FindConfigPath(startDirectory);
        if (configPath is null)
        {
            throw new InvalidOperationException(
                $"No --startup-project was specified and '{MigratorCliConfig.FileName}' was not found. " +
                "Pass --startup-project <PATH> or run 'db-migrator config --generate' at the solution root.");
        }

        var configDir = Path.GetDirectoryName(configPath)!;
        var config = MigratorCliConfig.Load(configPath);
        var interactive = picker is not null || InteractiveProjectPicker.IsInteractive(settings.NonInteractive);
        var choose = picker ?? InteractiveProjectPicker.PickProject;

        // 1. Resolve startup project
        var startupFullPath = ResolveStartupProject(config, configDir, interactive, choose);

        // 2. Resolve destination project
        string? destinationFullPath;
        if (explicitDestination)
        {
            destinationFullPath = settings.DestinationProjectPath;
        }
        else if (!string.IsNullOrWhiteSpace(settings.OutputDir))
        {
            destinationFullPath = null;
        }
        else
        {
            destinationFullPath = ResolveDestinationProject(config, configDir, startupFullPath, interactive, choose);
        }

        return new ResolvedProjectPaths(startupFullPath, destinationFullPath);
    }

    private static string ResolveStartupProject(
        MigratorCliConfig config,
        string configDir,
        bool interactive,
        Func<string, IReadOnlyList<ProjectChoice>, ProjectChoice> choose)
    {
        if (!string.IsNullOrWhiteSpace(config.DefaultStartupProject))
        {
            var defaultFull = MigratorCliConfig.ResolveAbsolutePath(configDir, config.DefaultStartupProject);
            if (!File.Exists(defaultFull))
            {
                throw new InvalidOperationException(
                    $"defaultStartupProject '{config.DefaultStartupProject}' in '{MigratorCliConfig.FileName}' does not exist. " +
                    "Run 'db-migrator config --generate' to refresh the config.");
            }

            return defaultFull;
        }

        var choices = BuildChoices(
            configDir,
            (config.StartupProjects, "startup"),
            (config.OtherProjects, "other"));

        if (choices.Count == 0)
        {
            throw new InvalidOperationException(
                $"No startup projects are configured in '{MigratorCliConfig.FileName}'. " +
                "Run 'db-migrator config --generate' or pass --startup-project <PATH>.");
        }

        if (choices.Count == 1)
        {
            return choices[0].FullPath;
        }

        if (!interactive)
        {
            throw new InvalidOperationException(
                $"Multiple startup projects exist in '{MigratorCliConfig.FileName}' and no defaultStartupProject is set. " +
                "Pass --startup-project <PATH> or run 'db-migrator config --set-default-startup <PATH>'.");
        }

        return choose("Select startup project", choices).FullPath;
    }

    private static string? ResolveDestinationProject(
        MigratorCliConfig config,
        string configDir,
        string resolvedStartupFullPath,
        bool interactive,
        Func<string, IReadOnlyList<ProjectChoice>, ProjectChoice> choose)
    {
        var choices = BuildChoices(
            configDir,
            (config.Projects, "registry"),
            (config.OtherProjects, "other"));

        // Include the selected startup project as a destination option if not already listed
        var startupRel = MigratorCliConfig.NormalizeRelativePath(configDir, resolvedStartupFullPath);
        if (!choices.Any(c => string.Equals(c.FullPath, resolvedStartupFullPath, StringComparison.OrdinalIgnoreCase)))
        {
            choices.Insert(0, new ProjectChoice(
                Path.GetFileNameWithoutExtension(resolvedStartupFullPath),
                startupRel,
                resolvedStartupFullPath,
                "startup"));
        }

        if (choices.Count == 1)
        {
            return choices[0].FullPath;
        }

        if (!interactive)
        {
            // In CI/non-interactive mode, default destination to the resolved startup project
            return resolvedStartupFullPath;
        }

        return choose("Select destination project", choices).FullPath;
    }

    private static List<ProjectChoice> BuildChoices(
        string configDir,
        params (IReadOnlyList<string> Paths, string Tag)[] groups)
    {
        var results = new List<ProjectChoice>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (paths, tag) in groups)
        {
            foreach (var raw in paths)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                var full = MigratorCliConfig.ResolveAbsolutePath(configDir, raw);
                if (!File.Exists(full) || !seen.Add(full))
                {
                    continue;
                }

                var rel = MigratorCliConfig.NormalizeRelativePath(configDir, full);
                var name = Path.GetFileNameWithoutExtension(full);
                results.Add(new ProjectChoice(name, rel, full, tag));
            }
        }

        return results;
    }
}
