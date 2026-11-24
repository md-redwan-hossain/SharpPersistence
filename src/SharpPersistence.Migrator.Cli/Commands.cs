using System.ComponentModel;
using SharpPersistence.Migrator.EfCore;
using SharpPersistence.Migrator.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SharpPersistence.Migrator.Cli;

internal class SharedSettings : CommandSettings
{
    [CommandOption("-s|--startup-project <PATH>")]
    [Description("Startup .csproj file or directory containing IDesignTimeMigrationFactory (auto-detected from current directory if omitted).")]
    public string? StartupProjectPath { get; init; }

    [CommandOption("-d|--destination-project <PATH>")]
    [Description("Destination .csproj file or directory where MigrationScripts and EntityChangelogs live (defaults to startup project directory).")]
    public string? DestinationProjectPath { get; init; }

    [CommandOption("-o|--output-dir <DIR>")]
    [Description("Optional output directory override for MigrationScripts and EntityChangelogs.")]
    public string? OutputDir { get; init; }

    [CommandOption("--no-build")]
    [Description("Skip running 'dotnet build' on the startup project before loading it.")]
    public bool NoBuild { get; init; }
}

internal sealed class DiffSettings : SharedSettings
{
    [CommandOption("-n|--name <NAME>")]
    [Description("Migration name (required).")]
    public string MigrationName { get; init; } = string.Empty;

    [CommandOption("-r|--registry <REGISTRY>")]
    [Description("Optional registry class name to filter.")]
    public string? RegistryFilter { get; init; }

    [CommandOption("--ci|--non-interactive")]
    [Description("Disable interactive rename prompts.")]
    public bool NonInteractive { get; init; }

    [CommandOption("--allow-empty")]
    [Description("Generate an empty migration script when no schema changes are detected.")]
    public bool AllowEmpty { get; init; }

    [CommandOption("--dry-run")]
    [Description("Preview generated SQL without writing files or updating snapshots.")]
    public bool DryRun { get; init; }

    public override ValidationResult Validate() =>
        string.IsNullOrWhiteSpace(MigrationName)
            ? ValidationResult.Error("Missing required option: --name <NAME>.")
            : ValidationResult.Success();
}

internal sealed class MigrateSettings : SharedSettings
{
    [CommandOption("--dry-run")]
    [Description("Preview pending scripts without executing against the database.")]
    public bool DryRun { get; init; }

    [CommandOption("--sync-ef-history")]
    [Description("Sync applied entries from __EFMigrationsHistory before running migrations.")]
    public bool SyncEfHistory { get; init; }
}

internal sealed class TranslateEfSettings : SharedSettings
{
    [CommandArgument(0, "<MIGRATIONS_DIR>")]
    [Description("Path to the EF Core Migrations folder containing *.cs migration files.")]
    public string MigrationsDirectory { get; init; } = string.Empty;

    [CommandOption("--dry-run")]
    [Description("Preview translated SQL scripts without writing files.")]
    public bool DryRun { get; init; }
}

internal sealed class DryRunSettings : SharedSettings
{
    [CommandOption("--dry-run")]
    [Description("Preview without writing changes.")]
    public bool DryRun { get; init; }
}

internal sealed class DoctorSettings : SharedSettings
{
    [CommandOption("-r|--registry <REGISTRY>")]
    [Description("Optional registry class name to check.")]
    public string? RegistryFilter { get; init; }

    [CommandOption("--check-db")]
    [Description("Also verify live database connectivity, migration history, and schema sync.")]
    public bool CheckDb { get; init; }
}

internal sealed class TranslateEfConfigsSettings : CommandSettings
{
    [CommandArgument(0, "<CONFIGS_DIR>")]
    [Description("Directory containing EF Core IEntityTypeConfiguration<T> .cs files.")]
    public string ConfigsDirectory { get; init; } = string.Empty;

    [CommandOption("-o|--output-dir <DIR>")]
    [Description("Output directory for translated IEntityConfiguration<T> files (defaults to overwriting in place).")]
    public string? OutputDir { get; init; }

    [CommandOption("--no-attribute")]
    [Description("Do not add [EntityConfiguration<T>] attributes to translated classes.")]
    public bool NoAttribute { get; init; }

    [CommandOption("--dry-run")]
    [Description("Preview translated files without writing to disk.")]
    public bool DryRun { get; init; }
}

internal sealed class DiffCommand : AsyncCommand<DiffSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DiffSettings settings)
    {
        var (owner, manager, _) = TargetAssemblyLoader.CreateManager(settings);
        try
        {
            Func<string, bool>? confirmRename = settings.NonInteractive
                ? null
                : prompt => AnsiConsole.Confirm($"[yellow]{Markup.Escape(prompt)}[/]", defaultValue: false);

            var generated = await manager.GenerateAsync(
                settings.MigrationName,
                settings.RegistryFilter,
                settings.AllowEmpty,
                interactiveRenamePrompt: !settings.NonInteractive,
                dryRun: settings.DryRun,
                confirmRename: confirmRename);

            if (generated.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]No schema changes detected.[/]");
                return 0;
            }

            foreach (var g in generated)
            {
                var badge = settings.DryRun ? "[yellow][[DRY-RUN]][/] " : string.Empty;
                if (g.ScriptPath is not null)
                {
                    AnsiConsole.MarkupLine($"{badge}[cyan][[{Markup.Escape(g.RegistryName)}]][/] {(settings.DryRun ? "Preview" : "Generated")}: [green]{Markup.Escape(g.ScriptPath)}[/]");
                    if (settings.DryRun && !string.IsNullOrWhiteSpace(g.SqlScript))
                    {
                        AnsiConsole.WriteLine(g.SqlScript);
                    }
                }

                if (g.DeferredScriptPath is not null)
                {
                    AnsiConsole.MarkupLine($"{badge}[cyan][[{Markup.Escape(g.RegistryName)}]][/] Deferred circular FK: [green]{Markup.Escape(g.DeferredScriptPath)}[/]");
                    if (settings.DryRun && !string.IsNullOrWhiteSpace(g.DeferredSqlScript))
                    {
                        AnsiConsole.WriteLine(g.DeferredSqlScript);
                    }
                }
            }

            return 0;
        }
        finally
        {
            await TargetAssemblyLoader.DisposeAsync(owner);
        }
    }
}

internal sealed class TranslateEfConfigsCommand : Command<TranslateEfConfigsSettings>
{
    public override int Execute(CommandContext context, TranslateEfConfigsSettings settings)
    {
        var inputDir = Path.GetFullPath(settings.ConfigsDirectory);
        var translated = EfCoreConfigTranslator.TranslateDirectory(
            inputDir,
            settings.OutputDir,
            dryRun: settings.DryRun,
            addEntityConfigurationAttribute: !settings.NoAttribute);

        if (translated.Count == 0)
        {
            AnsiConsole.MarkupLine($"[grey]No IEntityTypeConfiguration<T> classes found in: {Markup.Escape(inputDir)}[/]");
            return 0;
        }

        var badge = settings.DryRun ? "[yellow][[DRY-RUN]][/] " : string.Empty;
        foreach (var item in translated)
        {
            AnsiConsole.MarkupLine(
                $"{badge}[cyan][[{Markup.Escape(item.EntityTypeName)}]][/] {(settings.DryRun ? "Preview" : "Translated")}: [green]{Markup.Escape(item.RelativePath)}[/]");

            foreach (var warning in item.Warnings)
            {
                AnsiConsole.MarkupLine($"  [yellow]WARN:[/] {Markup.Escape(warning)}");
            }

            if (settings.DryRun)
            {
                AnsiConsole.WriteLine(item.TranslatedSource);
            }
        }

        return 0;
    }
}

internal sealed class VerifyCommand : AsyncCommand<SharedSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SharedSettings settings)
    {
        var (owner, manager, _) = TargetAssemblyLoader.CreateManager(settings);
        try
        {
            var drift = await manager.VerifySchemaAsync();
            if (drift.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]Schema matches the target database (0 drift items).[/]");
                return 0;
            }

            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("Registry")
                .AddColumn("Table")
                .AddColumn("Column")
                .AddColumn("Kind")
                .AddColumn("Details");

            foreach (var item in drift)
            {
                var kindMarkup = item.Kind switch
                {
                    "MissingTable" or "MissingColumn" => $"[red]{Markup.Escape(item.Kind)}[/]",
                    _ => $"[yellow]{Markup.Escape(item.Kind)}[/]"
                };

                table.AddRow(
                    Markup.Escape(item.RegistryName),
                    Markup.Escape(item.TableName),
                    Markup.Escape(item.ColumnName ?? "-"),
                    kindMarkup,
                    Markup.Escape(item.Details));
            }

            AnsiConsole.Write(table);
            return 1;
        }
        finally
        {
            await TargetAssemblyLoader.DisposeAsync(owner);
        }
    }
}

internal sealed class MigrateCommand : AsyncCommand<MigrateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, MigrateSettings settings)
    {
        var (owner, manager, _) = TargetAssemblyLoader.CreateManager(settings);
        try
        {
            if (settings.SyncEfHistory)
            {
                var synced = await manager.SyncEfCoreHistoryAsync(dryRun: settings.DryRun);
                var syncPrefix = settings.DryRun ? "[yellow][[DRY-RUN]] Would sync[/]" : "[blue]Synced[/]";
                foreach (var entry in synced)
                {
                    AnsiConsole.MarkupLine($"{syncPrefix}: {Markup.Escape(entry)}");
                }
            }

            var applied = await manager.MigrateAsync(dryRun: settings.DryRun);
            if (applied.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]No pending migrations.[/]");
                return 0;
            }

            var prefix = settings.DryRun ? "[yellow][[DRY-RUN]] Would apply[/]" : "[green]Applied[/]";
            foreach (var script in applied)
            {
                AnsiConsole.MarkupLine($"{prefix}: {Markup.Escape(script)}");
            }

            return 0;
        }
        finally
        {
            await TargetAssemblyLoader.DisposeAsync(owner);
        }
    }
}

internal sealed class StatusCommand : AsyncCommand<SharedSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SharedSettings settings)
    {
        var (owner, manager, _) = TargetAssemblyLoader.CreateManager(settings);
        try
        {
            var statuses = await manager.GetStatusAsync();
            if (statuses.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]No migration scripts found.[/]");
                return 0;
            }

            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("Registry")
                .AddColumn("Script")
                .AddColumn("Status")
                .AddColumn("Applied At (UTC)");

            foreach (var item in statuses)
            {
                var stateMarkup = item.IsApplied
                    ? (item.HasChecksumMismatch ? "[red]CHECKSUM MISMATCH[/]" : "[green]APPLIED[/]")
                    : "[yellow]PENDING[/]";

                table.AddRow(
                    Markup.Escape(item.RegistryName),
                    Markup.Escape(item.ScriptName),
                    stateMarkup,
                    item.AppliedAt?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "-");
            }

            AnsiConsole.Write(table);
            return 0;
        }
        finally
        {
            await TargetAssemblyLoader.DisposeAsync(owner);
        }
    }
}

internal sealed class PendingCommand : AsyncCommand<SharedSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SharedSettings settings)
    {
        var (owner, manager, _) = TargetAssemblyLoader.CreateManager(settings);
        try
        {
            var pending = await manager.GetPendingMigrationsAsync();
            if (pending.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]No pending migrations.[/]");
                return 0;
            }

            foreach (var p in pending)
            {
                AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(p)}[/]");
            }

            return 0;
        }
        finally
        {
            await TargetAssemblyLoader.DisposeAsync(owner);
        }
    }
}

internal sealed class TranslateEfMigrationCommand : AsyncCommand<TranslateEfSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, TranslateEfSettings settings)
    {
        var (owner, manager, _) = TargetAssemblyLoader.CreateManager(settings);
        try
        {
            var dir = Path.GetFullPath(settings.MigrationsDirectory);
            var translated = await manager.TranslateEfCoreMigrationsAsync(dir, dryRun: settings.DryRun);
            if (translated.Count == 0)
            {
                AnsiConsole.MarkupLine($"[grey]No EF Core migrations found in: {Markup.Escape(dir)}[/]");
                return 0;
            }

            var mode = settings.DryRun ? "[yellow][[DRY-RUN]][/] " : string.Empty;
            foreach (var item in translated)
            {
                AnsiConsole.MarkupLine($"{mode}[cyan][[{Markup.Escape(item.RegistryName)}]][/] {(settings.DryRun ? "Preview" : "Translated")}: [green]{Markup.Escape(item.ScriptPath ?? string.Empty)}[/]");
                if (settings.DryRun && !string.IsNullOrWhiteSpace(item.SqlScript))
                {
                    AnsiConsole.WriteLine(item.SqlScript);
                }
            }

            return 0;
        }
        finally
        {
            await TargetAssemblyLoader.DisposeAsync(owner);
        }
    }
}

internal sealed class SyncEfCoreHistoryCommand : AsyncCommand<DryRunSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DryRunSettings settings)
    {
        var (owner, manager, _) = TargetAssemblyLoader.CreateManager(settings);
        try
        {
            var synced = await manager.SyncEfCoreHistoryAsync(dryRun: settings.DryRun);
            if (synced.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]No EF Core history entries to sync.[/]");
                return 0;
            }

            var prefix = settings.DryRun ? "[yellow][[DRY-RUN]] Would mark applied[/]" : "[green]Marked applied[/]";
            foreach (var entry in synced)
            {
                AnsiConsole.MarkupLine($"{prefix}: {Markup.Escape(entry)}");
            }

            return 0;
        }
        finally
        {
            await TargetAssemblyLoader.DisposeAsync(owner);
        }
    }
}

internal sealed class DoctorCommand : AsyncCommand<DoctorSettings>
{
    private const string PassIcon = "\u2713";
    private const string FailIcon = "\u2717";
    private const string ArrowIcon = "\u2192";
    private const string DotIcon = "\u2022";

    public override async Task<int> ExecuteAsync(CommandContext context, DoctorSettings settings)
    {
        LoadedMigrationContext loaded;
        try
        {
            loaded = TargetAssemblyLoader.CreateManagerContext(settings);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[bold]SharpPersistence Migrator Doctor[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold underline]DI & Wiring[/]");
            AnsiConsole.MarkupLine($"  [red]{FailIcon}[/] Failed to bootstrap DI container: {Markup.Escape(ex.Message)}");
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule().RuleStyle("grey"));
            AnsiConsole.MarkupLine("Summary: [green]0 passed[/], [yellow]0 warnings[/], [red]1 error[/]  (exit code 1)");
            return 1;
        }

        try
        {
            var report = await loaded.Manager.DiagnoseAsync(
                settings.RegistryFilter,
                checkDatabase: settings.CheckDb,
                discoveredAssemblies: loaded.LoadedAssemblies);

            var modeLabel = settings.CheckDb ? "Live DB" : "Offline";
            AnsiConsole.MarkupLine("[bold]SharpPersistence Migrator Doctor[/]");
            AnsiConsole.MarkupLine(
                $"[grey]Host: {Markup.Escape(loaded.HostName)} ({report.Dialect})  {DotIcon}  Mode: {modeLabel}  {DotIcon}  Registries: {report.RegistryReports.Count}[/]");
            AnsiConsole.WriteLine();

            // DI & Wiring
            AnsiConsole.MarkupLine("[bold]DI & Wiring[/]");
            AnsiConsole.MarkupLine($"  [green]{PassIcon}[/] ServiceProvider bootstrapped ({Markup.Escape(loaded.BootstrapSource)})");
            RenderItems(report.WiringChecks);

            // Per-Registry summary
            foreach (var reg in report.RegistryReports)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine(
                    $"[bold]Registry: {Markup.Escape(reg.RegistryName)}[/] [grey]({reg.EntityCount} entities, {reg.ColumnCount} columns)[/]");
                RenderItems(reg.Items);
            }

            // Cross-Registry Checks
            if (report.CrossRegistryChecks.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]Cross-Registry Checks[/]");
                RenderItems(report.CrossRegistryChecks);
            }

            // Migration Scripts Coverage
            if (report.MigrationCoverageChecks.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]Migration Scripts[/]");
                RenderItems(report.MigrationCoverageChecks);
            }

            // Live Database
            if (report.DatabaseChecks.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]Live Database[/]");
                RenderItems(report.DatabaseChecks);
            }

            var totalPassed = report.PassedCount + 1; // includes ServiceProvider bootstrapped
            var exitCode = report.HasErrors ? 1 : 0;

            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule().RuleStyle("grey"));
            AnsiConsole.MarkupLine(
                $"Summary: [green]{totalPassed} passed[/], [yellow]{report.WarningCount} {(report.WarningCount == 1 ? "warning" : "warnings")}[/], [red]{report.ErrorCount} {(report.ErrorCount == 1 ? "error" : "errors")}[/]  (exit code {exitCode})");

            if (!settings.CheckDb)
            {
                AnsiConsole.MarkupLine("[grey]Tip: Pass --check-db to also verify live database connectivity and schema sync.[/]");
            }

            return exitCode;
        }
        finally
        {
            await TargetAssemblyLoader.DisposeAsync(loaded.ServiceScopeOwner);
        }
    }

    private static void RenderItems(IReadOnlyList<DoctorCheckItem> items)
    {
        var ungrouped = items.Where(i => string.IsNullOrWhiteSpace(i.EntityName));
        foreach (var item in ungrouped)
        {
            RenderSingleItem(item, indent: "  ");
        }

        var groupedByEntity = items
            .Where(i => !string.IsNullOrWhiteSpace(i.EntityName))
            .GroupBy(i => i.EntityName!, StringComparer.Ordinal);

        foreach (var group in groupedByEntity)
        {
            var worst = group.Max(i => i.Severity);
            var badge = worst == DoctorSeverity.Error ? $"[red]{FailIcon}[/]" : "[yellow]![/]";
            AnsiConsole.MarkupLine($"  {badge} [bold]{Markup.Escape(group.Key)}[/]");
            foreach (var item in group)
            {
                var itemBadge = item.Severity == DoctorSeverity.Error ? $"[red]{FailIcon}[/]" : "[yellow]![/]";
                AnsiConsole.MarkupLine($"      {itemBadge} {Markup.Escape(item.Message)}");
                if (!string.IsNullOrWhiteSpace(item.Hint))
                {
                    AnsiConsole.MarkupLine($"        [grey]{ArrowIcon} {Markup.Escape(item.Hint)}[/]");
                }
            }
        }
    }

    private static void RenderSingleItem(DoctorCheckItem item, string indent)
    {
        var badge = item.Severity switch
        {
            DoctorSeverity.Pass => $"[green]{PassIcon}[/]",
            DoctorSeverity.Warning => "[yellow]![/]",
            _ => $"[red]{FailIcon}[/]"
        };

        AnsiConsole.MarkupLine($"{indent}{badge} {Markup.Escape(item.Message)}");
        if (!string.IsNullOrWhiteSpace(item.Hint))
        {
            AnsiConsole.MarkupLine($"{indent}    [grey]{ArrowIcon} {Markup.Escape(item.Hint)}[/]");
        }
    }
}
