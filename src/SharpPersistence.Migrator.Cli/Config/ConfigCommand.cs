using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SharpPersistence.Migrator.Cli.Config;

internal sealed class ConfigSettings : CommandSettings
{
    [CommandOption("-g|--generate")]
    [Description("Scan the solution and generate or refresh db-migrator-config.json at the solution root.")]
    public bool Generate { get; init; }

    [CommandOption("--set-default [PATH]")]
    [Description("Set defaultProject in db-migrator-config.json (omit PATH to pick interactively).")]
    public FlagValue<string>? SetDefault { get; init; }

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

        if (settings.SetDefault is { IsSet: true })
        {
            return ExecuteSetDefault(settings);
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

        if (string.IsNullOrWhiteSpace(result.Config.DefaultProject) &&
            result.Config.Projects.Count > 1 &&
            SpectreProjectPicker.Instance.IsInteractive(settings.NonInteractive))
        {
            var choices = result.Config.Projects
                .Select(rel =>
                {
                    var full = MigratorCliConfig.ResolveAbsolutePath(solutionRoot, rel);
                    return new ProjectChoice(Path.GetFileNameWithoutExtension(full), rel, full, "migrator");
                })
                .ToList();

            var selected = SpectreProjectPicker.Instance.PickProject("Select defaultProject", choices);
            result.Config.DefaultProject = selected.RelativePath;
        }

        result.Config.Save(result.ConfigPath);

        var verb = existing is null ? "Generated" : "Updated";
        AnsiConsole.MarkupLine($"[green]{verb}[/] [cyan]{Markup.Escape(result.ConfigPath)}[/]");
        RenderConfigSummary(result.Config);

        if (result.Config.Projects.Count == 0)
        {
            AnsiConsole.MarkupLine(
                "[yellow]Hint:[/] No migrator projects (with [[assembly: EntityRegistryRoot]] or IDesignTimeDbMigratorFactory) were detected. " +
                "You can add project paths to [cyan]otherProjects[/] or pass [cyan]--project <PATH>[/].");
        }

        return 0;
    }

    private static int ExecuteSetDefault(ConfigSettings settings)
    {
        var configPath = MigratorCliConfig.FindConfigPath();
        if (configPath is null)
        {
            AnsiConsole.MarkupLine(
                $"[red]Error:[/] '{MigratorCliConfig.FileName}' not found. Run 'db-migrator config --generate' at the solution root first.");
            return 1;
        }

        var configDir = Path.GetDirectoryName(configPath)!;
        var config = MigratorCliConfig.Load(configPath);
        string selectedFullPath;

        var rawArg = settings.SetDefault?.Value;
        if (!string.IsNullOrWhiteSpace(rawArg))
        {
            selectedFullPath = Path.IsPathRooted(rawArg)
                ? Path.GetFullPath(rawArg)
                : File.Exists(Path.GetFullPath(rawArg))
                    ? Path.GetFullPath(rawArg)
                    : MigratorCliConfig.ResolveAbsolutePath(configDir, rawArg);

            if (!File.Exists(selectedFullPath))
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Project not found: '{Markup.Escape(rawArg)}'.");
                return 1;
            }
        }
        else
        {
            if (!SpectreProjectPicker.Instance.IsInteractive(settings.NonInteractive))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Pass a project path to --set-default in non-interactive mode.");
                return 1;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var choices = new List<ProjectChoice>();
            foreach (var (list, tag) in new (IReadOnlyList<string>, string)[]
                     {
                         (config.Projects, "migrator"),
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
                    $"[red]Error:[/] No migrator projects listed in '{MigratorCliConfig.FileName}'. Run 'db-migrator config --generate' first.");
                return 1;
            }

            selectedFullPath = SpectreProjectPicker.Instance.PickProject("Select defaultProject", choices).FullPath;
        }

        var normalizedRel = MigratorCliConfig.NormalizeRelativePath(configDir, selectedFullPath);
        config.DefaultProject = normalizedRel;
        if (!config.Projects.Contains(normalizedRel, StringComparer.OrdinalIgnoreCase) &&
            !config.OtherProjects.Contains(normalizedRel, StringComparer.OrdinalIgnoreCase))
        {
            config.Projects.Add(normalizedRel);
        }

        config.Save(configPath);
        AnsiConsole.MarkupLine($"[green]Updated defaultProject[/] -> [cyan]{Markup.Escape(normalizedRel)}[/]");
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
            "[bold]defaultProject[/]",
            string.IsNullOrWhiteSpace(config.DefaultProject)
                ? "[grey](none)[/]"
                : $"[green]{Markup.Escape(config.DefaultProject)}[/]");

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
