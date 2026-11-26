using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SharpPersistence.Migrator.Cli.Translators;

internal sealed class AnnotateDbContextEntitiesSettings : CommandSettings
{
    [CommandOption("--dbcontext <PATH>")]
    [Description("Path to an EF Core DbContext .cs file or directory containing DbContext files.")]
    public string DbContextPath { get; init; } = string.Empty;

    [CommandOption("--search-directory <DIR>")]
    [Description("Directory to recursively search for matching entity .cs files.")]
    public string SearchDirectory { get; init; } = string.Empty;

    [CommandOption("--entity-group-name <NAME>")]
    [Description("Optional IEntityGroup struct name to emit [Entity<TGroup>]. Defaults to [Entity].")]
    public string? EntityGroupName { get; init; }

    [CommandOption("--entity-group-namespace <NAMESPACE>")]
    [Description("Optional namespace of the IEntityGroup struct to add as a using directive (requires --entity-group-name).")]
    public string? EntityGroupNamespace { get; init; }

    [CommandOption("--read-only")]
    [Description("Emit ReadOnly = true on the marker attribute (e.g. [Entity(ReadOnly = true)]).")]
    public bool ReadOnly { get; init; }

    [CommandOption("--dry-run")]
    [Description("Preview which entity files would be annotated without writing to disk.")]
    public bool DryRun { get; init; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(DbContextPath))
            return ValidationResult.Error("--dbcontext <PATH> is required.");
        if (string.IsNullOrWhiteSpace(SearchDirectory))
            return ValidationResult.Error("--search-directory <DIR> is required.");
        var hasGroupName = !string.IsNullOrWhiteSpace(EntityGroupName);
        var hasGroupNs = !string.IsNullOrWhiteSpace(EntityGroupNamespace);
        if (hasGroupName != hasGroupNs)
            return ValidationResult.Error("--entity-group-name and --entity-group-namespace must be specified together.");
        return ValidationResult.Success();
    }
}

internal sealed class AnnotateDbContextEntitiesCommand : Command<AnnotateDbContextEntitiesSettings>
{
    public override int Execute(CommandContext context, AnnotateDbContextEntitiesSettings settings)
    {
        var result = EfCoreEntityAnnotator.AnnotateDirectory(
            settings.DbContextPath,
            settings.SearchDirectory,
            settings.EntityGroupName,
            settings.EntityGroupNamespace,
            settings.ReadOnly,
            settings.DryRun);

        var attrDisplay = settings.ReadOnly
            ? $"[{result.MarkerAttribute.AttributeShortName}(ReadOnly = true)]"
            : $"[{result.MarkerAttribute.AttributeShortName}]";

        var prefix = settings.DryRun ? "[yellow]DRY RUN:[/] " : string.Empty;
        var nsInfo = !string.IsNullOrWhiteSpace(result.MarkerAttribute.AttributeNamespace)
            ? $" (using {Markup.Escape(result.MarkerAttribute.AttributeNamespace)})"
            : string.Empty;

        AnsiConsole.MarkupLine($"{prefix}Marker attribute: [cyan]{Markup.Escape(attrDisplay)}[/]{nsInfo}");

        foreach (var warning in result.Warnings)
            AnsiConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(warning)}");

        if (result.Items.Count > 0)
        {
            var table = new Table().AddColumn("Entity").AddColumn("File").AddColumn("Status");
            foreach (var item in result.Items)
            {
                var status = item.AlreadyAnnotated
                    ? "[grey]already annotated[/]"
                    : settings.DryRun ? "[yellow]would annotate[/]" : "[green]annotated[/]";
                table.AddRow(Markup.Escape(item.EntityName), Markup.Escape(item.RelativePath), status);
            }
            AnsiConsole.Write(table);
        }

        if (result.UnmatchedEntityNames.Count > 0)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]Unmatched DbContext entities ({result.UnmatchedEntityNames.Count}):[/] " +
                Markup.Escape(string.Join(", ", result.UnmatchedEntityNames)));
        }

        var modifiedCount = result.Items.Count(i => i.Modified);
        var skippedCount = result.Items.Count(i => i.AlreadyAnnotated);
        var verb = settings.DryRun ? "Would annotate" : "Annotated";
        AnsiConsole.MarkupLine(
            $"{prefix}{verb} [green]{modifiedCount}[/] entity class(es), skipped [grey]{skippedCount}[/] already annotated, [yellow]{result.UnmatchedEntityNames.Count}[/] unmatched.");

        return 0;
    }
}
