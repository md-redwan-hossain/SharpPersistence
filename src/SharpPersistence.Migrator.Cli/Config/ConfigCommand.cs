using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SharpPersistence.Migrator.Cli.Config;

internal sealed class ConfigSettings : CommandSettings
{
    [CommandOption("-g|--generate")]
    [Description("Scan the solution and generate or refresh db-migrator-config.json at the solution root.")]
    public bool Generate { get; init; }

    [CommandOption("--set-default-startup [PATH]")]
    [Description("Set defaultStartupProject in db-migrator-config.json (omit PATH to pick interactively).")]
    public FlagValue<string>? SetDefaultStartup { get; init; }

    [CommandOption("--set-default-destination [PATH]")]
    [Description("Set defaultDestinationProject in db-migrator-config.json (omit PATH to pick interactively).")]
    public FlagValue<string>? SetDefaultDestination { get; init; }

    [CommandOption("--show")]
    [Description("Display the current db-migrator-config.json.")]
    public bool Show { get; init; }

    [CommandOption("--ci|--non-interactive")]
    [Description("Disable interactive prompts.")]
    public bool NonInteractive { get; init; }
}

internal sealed class ConfigCommand : Command<ConfigSettings>
{
    public override int Execute(CommandContext context, ConfigSettings settings)
    {
        if (settings.Generate)
        {
            return ExecuteGenerate(settings);
        }

        if (settings.SetDefaultStartup is { IsSet: true })
        {
            return ExecuteSetDefaultStartup(settings);
        }

        if (settings.SetDefaultDestination is { IsSet: true })
        {
            return ExecuteSetDefaultDestination(settings);
        }

        return ExecuteShow();
    }

    private static int ExecuteGenerate(ConfigSettings settings)
    {
        var solutionRoot = SolutionProjectScanner.FindSolutionRoot();
        var configPath = Path.Combine(solutionRoot, MigratorCliConfig.FileName);
        MigratorCliConfig? existing = null;

        if (File.Exists(configPath))
        {
            existing = MigratorCliConfig.Load(configPath);
        }

        var result = SolutionProjectScanner.GenerateOrRefresh(solutionRoot, existing);

        foreach (var warning in result.Warnings)
        {
            AnsiConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(warning)}");
        }

        if (string.IsNullOrWhiteSpace(result.Config.DefaultStartupProject) &&
            result.Config.StartupProjects.Count > 1 &&
            SpectreProjectPicker.Instance.IsInteractive(settings.NonInteractive))
        {
            var choices = result.Config.StartupProjects
                .Select(rel =>
                {
                    var full = MigratorCliConfig.ResolveAbsolutePath(solutionRoot, rel);
                    return new ProjectChoice(Path.GetFileNameWithoutExtension(full), rel, full, "startup");
                })
                .ToList();

            var selected = SpectreProjectPicker.Instance.PickProject("Select defaultStartupProject", choices);
            result.Config.DefaultStartupProject = selected.RelativePath;
        }

        result.Config.Save(result.ConfigPath);

        var verb = existing is null ? "Generated" : "Updated";
        AnsiConsole.MarkupLine($"[green]{verb}[/] [cyan]{Markup.Escape(result.ConfigPath)}[/]");
        RenderConfigSummary(result.Config);
        return 0;
    }

    private static int ExecuteSetDefaultStartup(ConfigSettings settings)
    {
        var configPath = MigratorCliConfig.FindConfigPath();
        if (configPath is null)
        {
            AnsiConsole.MarkupLine(
                $"[red]Error:[/] '{MigratorCliConfig.FileName}' was not found. Run 'db-migrator config --generate' first.");
            return 1;
        }

        var configDir = Path.GetDirectoryName(configPath)!;
        var config = MigratorCliConfig.Load(configPath);
        string selectedFullPath;

        var rawArg = settings.SetDefaultStartup?.Value;
        if (!string.IsNullOrWhiteSpace(rawArg))
        {
            selectedFullPath = Path.IsPathRooted(rawArg)
                ? Path.GetFullPath(rawArg)
                : File.Exists(Path.GetFullPath(rawArg))
                    ? Path.GetFullPath(rawArg)
                    : MigratorCliConfig.ResolveAbsolutePath(configDir, rawArg);

            if (!File.Exists(selectedFullPath))
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Startup project not found: '{Markup.Escape(rawArg)}'.");
                return 1;
            }

            if (!SolutionProjectScanner.IsEligibleStartupProject(selectedFullPath))
            {
                AnsiConsole.MarkupLine(
                    $"[red]Error:[/] '{Markup.Escape(rawArg)}' is not an eligible migrator root project " +
                    "(requires <EntityRegistryRoot>true</EntityRegistryRoot>, [[assembly: EntityRegistryRoot]], AddDbMigrator, or IDesignTimeDbMigratorFactory).");
                return 1;
            }
        }
        else
        {
            if (!SpectreProjectPicker.Instance.IsInteractive(settings.NonInteractive))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Pass a project path to --set-default-startup in non-interactive mode.");
                return 1;
            }

            var choices = config.StartupProjects
                .Select(rel =>
                {
                    var full = MigratorCliConfig.ResolveAbsolutePath(configDir, rel);
                    return new ProjectChoice(Path.GetFileNameWithoutExtension(full), rel, full, "startup");
                })
                .Where(static c => File.Exists(c.FullPath))
                .ToList();

            if (choices.Count == 0)
            {
                AnsiConsole.MarkupLine(
                    $"[red]Error:[/] No startup projects listed in '{MigratorCliConfig.FileName}'. Run 'db-migrator config --generate' first.");
                return 1;
            }

            selectedFullPath = SpectreProjectPicker.Instance.PickProject("Select defaultStartupProject", choices).FullPath;
        }

        var normalizedRel = MigratorCliConfig.NormalizeRelativePath(configDir, selectedFullPath);
        config.DefaultStartupProject = normalizedRel;
        if (!config.StartupProjects.Contains(normalizedRel, StringComparer.OrdinalIgnoreCase))
        {
            config.StartupProjects.Add(normalizedRel);
            config.StartupProjects.Sort(StringComparer.OrdinalIgnoreCase);
        }

        config.Save(configPath);
        AnsiConsole.MarkupLine($"[green]Updated defaultStartupProject[/] → [cyan]{Markup.Escape(normalizedRel)}[/]");
        return 0;
    }

    private static int ExecuteSetDefaultDestination(ConfigSettings settings)
    {
        var configPath = MigratorCliConfig.FindConfigPath();
        if (configPath is null)
        {
            AnsiConsole.MarkupLine(
                $"[red]Error:[/] '{MigratorCliConfig.FileName}' was not found. Run 'db-migrator config --generate' first.");
            return 1;
        }

        var configDir = Path.GetDirectoryName(configPath)!;
        var config = MigratorCliConfig.Load(configPath);
        string selectedFullPath;

        var rawArg = settings.SetDefaultDestination?.Value;
        if (!string.IsNullOrWhiteSpace(rawArg))
        {
            selectedFullPath = Path.IsPathRooted(rawArg)
                ? Path.GetFullPath(rawArg)
                : File.Exists(Path.GetFullPath(rawArg))
                    ? Path.GetFullPath(rawArg)
                    : MigratorCliConfig.ResolveAbsolutePath(configDir, rawArg);

            if (!File.Exists(selectedFullPath))
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Destination project not found: '{Markup.Escape(rawArg)}'.");
                return 1;
            }
        }
        else
        {
            if (!SpectreProjectPicker.Instance.IsInteractive(settings.NonInteractive))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Pass a project path to --set-default-destination in non-interactive mode.");
                return 1;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var choices = new List<ProjectChoice>();
            foreach (var (list, tag) in new (IReadOnlyList<string>, string)[]
                     {
                         (config.StartupProjects, "startup"),
                         (config.Projects, "registry"),
                         (config.OtherProjects, "other")
                     })
            {
                foreach (var rel in list)
                {
                    var full = MigratorCliConfig.ResolveAbsolutePath(configDir, rel);
                    if (File.Exists(full) && seen.Add(full))
                    {
                        choices.Add(new ProjectChoice(Path.GetFileNameWithoutExtension(full), rel, full, tag));
                    }
                }
            }

            if (choices.Count == 0)
            {
                AnsiConsole.MarkupLine(
                    $"[red]Error:[/] No projects listed in '{MigratorCliConfig.FileName}'. Run 'db-migrator config --generate' first.");
                return 1;
            }

            selectedFullPath = SpectreProjectPicker.Instance.PickProject("Select defaultDestinationProject", choices).FullPath;
        }

        var normalizedRel = MigratorCliConfig.NormalizeRelativePath(configDir, selectedFullPath);
        config.DefaultDestinationProject = normalizedRel;
        config.Save(configPath);
        AnsiConsole.MarkupLine($"[green]Updated defaultDestinationProject[/] → [cyan]{Markup.Escape(normalizedRel)}[/]");
        return 0;
    }

    private static int ExecuteShow()
    {
        var configPath = MigratorCliConfig.FindConfigPath();
        if (configPath is null)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]No '{MigratorCliConfig.FileName}' found.[/] Run 'db-migrator config --generate' at the solution root.");
            return 1;
        }

        var config = MigratorCliConfig.Load(configPath);
        AnsiConsole.MarkupLine($"[bold]Config:[/] [cyan]{Markup.Escape(configPath)}[/]");
        RenderConfigSummary(config);
        return 0;
    }

    private static void RenderConfigSummary(MigratorCliConfig config)
    {
        var table = new Table()
            .AddColumn("Section")
            .AddColumn("Projects");

        table.AddRow(
            "[bold]defaultStartupProject[/]",
            string.IsNullOrWhiteSpace(config.DefaultStartupProject)
                ? "[grey](none)[/]"
                : $"[green]{Markup.Escape(config.DefaultStartupProject)}[/]");

        table.AddRow(
            "[bold]defaultDestinationProject[/]",
            string.IsNullOrWhiteSpace(config.DefaultDestinationProject)
                ? "[grey](none)[/]"
                : $"[green]{Markup.Escape(config.DefaultDestinationProject)}[/]");

        table.AddRow(
            "startupProjects",
            config.StartupProjects.Count == 0
                ? "[grey](none)[/]"
                : string.Join(Environment.NewLine, config.StartupProjects.Select(Markup.Escape)));

        table.AddRow(
            "projects",
            config.Projects.Count == 0
                ? "[grey](none)[/]"
                : string.Join(Environment.NewLine, config.Projects.Select(Markup.Escape)));

        table.AddRow(
            "otherProjects",
            config.OtherProjects.Count == 0
                ? "[grey](none)[/]"
                : string.Join(Environment.NewLine, config.OtherProjects.Select(Markup.Escape)));

        AnsiConsole.Write(table);
    }
}
