using Spectre.Console;

namespace SharpPersistence.Migrator.Cli.Config;

internal sealed record ProjectChoice(string DisplayName, string RelativePath, string FullPath, string Tag);

internal interface IProjectPicker
{
    bool IsInteractive(bool nonInteractiveFlag);
    ProjectChoice PickProject(string title, IReadOnlyList<ProjectChoice> choices);
}

internal sealed class SpectreProjectPicker : IProjectPicker
{
    public static readonly SpectreProjectPicker Instance = new();

    public bool IsInteractive(bool nonInteractiveFlag) =>
        !nonInteractiveFlag &&
        !Console.IsInputRedirected &&
        AnsiConsole.Profile.Capabilities.Interactive;

    public ProjectChoice PickProject(string title, IReadOnlyList<ProjectChoice> choices)
    {
        var prompt = new SelectionPrompt<ProjectChoice>()
            .Title($"[bold]{Markup.Escape(title)}[/]")
            .PageSize(12)
            .EnableSearch()
            .SearchPlaceholderText("[grey]Type to search...[/]")
            .MoreChoicesText("[grey](Move up and down to reveal more projects)[/]")
            .HighlightStyle(new Style(Color.Green, decoration: Decoration.Bold))
            .UseConverter(static c =>
                $"{Markup.Escape(c.DisplayName)} [cyan][[{Markup.Escape(c.Tag)}]][/] [grey]({Markup.Escape(c.RelativePath)})[/]")
            .AddChoices(choices);

        AnsiConsole.MarkupLine("[grey]↑/↓ to select • Enter: confirm • Type: to search[/]");
        return AnsiConsole.Prompt(prompt);
    }
}
