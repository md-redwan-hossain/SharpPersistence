namespace SharpPersistence.Migrator.Cli.Config;

internal static class ProjectResolutionService
{
    public static string Resolve(
        SharedSettings settings,
        string? startDirectory = null,
        IProjectPicker? picker = null)
    {
        var activePicker = picker ?? SpectreProjectPicker.Instance;

        // If user explicitly passed --project, honor it directly (no config required)
        if (!string.IsNullOrWhiteSpace(settings.ProjectPath))
        {
            return settings.ProjectPath!;
        }

        var configPath = MigratorCliConfig.FindConfigPath(startDirectory);
        if (configPath is null)
        {
            throw new InvalidOperationException(
                $"No --project was specified and '{MigratorCliConfig.FileName}' was not found. " +
                "Pass --project <PATH> or run 'db-migrator config --generate' at the solution root.");
        }

        var configDir = Path.GetDirectoryName(configPath)!;
        var config = MigratorCliConfig.Load(configPath);
        var interactive = activePicker.IsInteractive(settings.NonInteractive);

        return ResolveMigratorProject(config, configDir, interactive, activePicker);
    }

    private static string ResolveMigratorProject(
        MigratorCliConfig config,
        string configDir,
        bool interactive,
        IProjectPicker picker)
    {
        if (!string.IsNullOrWhiteSpace(config.DefaultProject))
        {
            var defaultFull = MigratorCliConfig.ResolveAbsolutePath(configDir, config.DefaultProject);
            if (!File.Exists(defaultFull))
            {
                throw new InvalidOperationException(
                    $"defaultProject '{config.DefaultProject}' in '{MigratorCliConfig.FileName}' does not exist. " +
                    "Run 'db-migrator config --generate' to refresh the config.");
            }

            return defaultFull;
        }

        var choices = BuildChoices(
            configDir,
            (config.Projects, "migrator"),
            (config.OtherProjects, "other"));

        if (choices.Count == 0)
        {
            throw new InvalidOperationException(
                $"No migrator projects are configured in '{MigratorCliConfig.FileName}'. " +
                "Run 'db-migrator config --generate' or pass --project <PATH>.");
        }

        if (choices.Count == 1)
        {
            return choices[0].FullPath;
        }

        if (!interactive)
        {
            throw new InvalidOperationException(
                $"Multiple migrator projects exist in '{MigratorCliConfig.FileName}' and no defaultProject is set. " +
                "Pass --project <PATH> or run 'db-migrator config --set-default <PATH>'.");
        }

        return picker.PickProject("Select migrator project", choices).FullPath;
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
