using System.ComponentModel;
using SharpPersistence.Migrator.Cli.Translators;
using SharpPersistence.Migrator.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SharpPersistence.Migrator.Cli;

internal class SharedSettings : CommandSettings
{
    [CommandOption("-s|--startup-project <PATH>")]
    [Description("Startup .csproj file or directory containing IDesignTimeDbMigratorFactory (auto-detected from current directory if omitted).")]
    public string? StartupProjectPath { get; init; }

    [CommandOption("-d|--destination-project <PATH>")]
    [Description("Destination .csproj file or directory where DbMigrator/Scripts and DbMigrator/Snapshots live (defaults to startup project directory).")]
    public string? DestinationProjectPath { get; init; }

    [CommandOption("--no-build")]
    [Description("Skip running 'dotnet build' on the startup project before loading it.")]
    public bool NoBuild { get; init; }

    [CommandOption("--ci|--non-interactive")]
    [Description("Disable interactive prompts (project pickers and rename prompts).")]
    public bool NonInteractive { get; init; }
}

internal sealed class DiffSettings : SharedSettings
{
    [CommandOption("-n|--name <NAME>")]
    [Description("Migration name (required).")]
    public string MigrationName { get; init; } = string.Empty;

    [CommandOption("-r|--registry <REGISTRY>")]
    [Description("Optional registry class name to filter.")]
    public string? RegistryFilter { get; init; }

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
    [CommandOption("--source-path <DIR>")]
    [Description("Path to the EF Core Migrations folder containing *.cs migration files (required).")]
    public string SourcePath { get; init; } = string.Empty;

    [CommandOption("--dry-run")]
    [Description("Preview translated SQL scripts without writing files.")]
    public bool DryRun { get; init; }

    public override ValidationResult Validate() =>
        string.IsNullOrWhiteSpace(SourcePath)
            ? ValidationResult.Error("Missing required option: --source-path <DIR>.")
            : ValidationResult.Success();
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

    [CommandOption("--runlist")]
    [Description("Run a focused check on db-migration-runlist.json, overrides, script IDs, and SQL DDL dependencies.")]
    public bool RunlistOnly { get; init; }
}

internal sealed class RunlistShowSettings : SharedSettings
{
    [CommandOption("--recent <COUNT>")]
    [Description("Show only the N most recent entries of the effective runlist.")]
    public int? Recent { get; init; }
}

internal sealed class RunlistSyncSettings : SharedSettings
{
    [CommandOption("--prune")]
    [Description("Remove orphaned script IDs from db-migration-runlist.json whose .sql files were deleted.")]
    public bool Prune { get; init; }

    [CommandOption("--dry-run")]
    [Description("Preview header stamps and runlist updates without writing files.")]
    public bool DryRun { get; init; }
}

internal sealed class TranslateEfConfigsSettings : CommandSettings
{
    [CommandOption("-s|--source-path <DIR>")]
    [Description("Directory containing EF Core IEntityTypeConfiguration<T> .cs files (required).")]
    public string SourcePath { get; init; } = string.Empty;

    [CommandOption("-o|--output-path <DIR>")]
    [Description("Optional output directory for translated IEntityConfiguration<T> files (defaults to <source-path>/TranslatedEfConfigs).")]
    public string? OutputPath { get; init; }

    [CommandOption("--no-attribute")]
    [Description("Do not add [EntityConfiguration<T>] attributes to translated classes.")]
    public bool NoAttribute { get; init; }

    [CommandOption("--dry-run")]
    [Description("Preview translated files without writing to disk.")]
    public bool DryRun { get; init; }

    public override ValidationResult Validate() =>
        string.IsNullOrWhiteSpace(SourcePath)
            ? ValidationResult.Error("Missing required option: --source-path <DIR>.")
            : ValidationResult.Success();
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

            var scriptCount = 0;
            foreach (var g in generated)
            {
                var badge = settings.DryRun ? "[yellow][[DRY-RUN]][/] " : string.Empty;
                if (g.RenamedSnapshotFrom is not null && g.SnapshotPath is not null)
                {
                    var renameVerb = settings.DryRun ? "Would rename snapshot" : "Renamed snapshot";
                    AnsiConsole.MarkupLine(
                        $"{badge}[cyan][[{Markup.Escape(g.RegistryName)}]][/] {renameVerb}: [yellow]{Markup.Escape(g.RenamedSnapshotFrom)}[/] -> [green]{Markup.Escape(g.SnapshotPath)}[/]");
                }
                else if (g.SnapshotPath is not null)
                {
                    var snapVerb = settings.DryRun ? "Would update snapshot" : "Updated snapshot";
                    AnsiConsole.MarkupLine(
                        $"{badge}[cyan][[{Markup.Escape(g.RegistryName)}]][/] {snapVerb}: [green]{Markup.Escape(g.SnapshotPath)}[/]");
                }

                if (g.ScriptPath is not null)
                {
                    scriptCount++;
                    AnsiConsole.MarkupLine($"{badge}[cyan][[{Markup.Escape(g.RegistryName)}]][/] {(settings.DryRun ? "Preview" : "Generated")}: [green]{Markup.Escape(g.ScriptPath)}[/]");
                    if (settings.DryRun && !string.IsNullOrWhiteSpace(g.SqlScript))
                    {
                        AnsiConsole.WriteLine(g.SqlScript);
                    }
                }

                if (g.DeferredScriptPath is not null)
                {
                    scriptCount++;
                    AnsiConsole.MarkupLine($"{badge}[cyan][[{Markup.Escape(g.RegistryName)}]][/] Deferred circular FK: [green]{Markup.Escape(g.DeferredScriptPath)}[/]");
                    if (settings.DryRun && !string.IsNullOrWhiteSpace(g.DeferredSqlScript))
                    {
                        AnsiConsole.WriteLine(g.DeferredSqlScript);
                    }
                }
            }

            if (scriptCount > 0)
            {
                var runlistPath = manager.CreateRunlistManager().RunlistPath;
                var runlistBadge = settings.DryRun ? "[yellow][[DRY-RUN]][/] " : string.Empty;
                var runlistVerb = settings.DryRun ? "Would append" : "Appended";
                AnsiConsole.MarkupLine(
                    $"{runlistBadge}{runlistVerb} {scriptCount} script(s) to [cyan]{Markup.Escape(runlistPath)}[/]");
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
        var inputDir = Path.GetFullPath(settings.SourcePath);
        var outputDir = EfCoreConfigTranslator.ResolveOutputDirectory(inputDir, settings.OutputPath);
        var translated = EfCoreConfigTranslator.TranslateDirectory(
            inputDir,
            settings.OutputPath,
            dryRun: settings.DryRun,
            addEntityConfigurationAttribute: !settings.NoAttribute);

        if (translated.Count == 0)
        {
            AnsiConsole.MarkupLine($"[grey]No IEntityTypeConfiguration<T> classes found in: {Markup.Escape(inputDir)}[/]");
            return 0;
        }

        var badge = settings.DryRun ? "[yellow][[DRY-RUN]][/] " : string.Empty;
        AnsiConsole.MarkupLine($"{badge}Output directory: [cyan]{Markup.Escape(outputDir)}[/]");
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
        var dir = PathGuard.EnsureReadableDirectory(settings.SourcePath, "--source-path");
        var (owner, manager, _) = TargetAssemblyLoader.CreateManager(settings);
        try
        {
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

            var runlistPath = manager.CreateRunlistManager().RunlistPath;
            var runlistVerb = settings.DryRun ? "Would append" : "Appended";
            AnsiConsole.MarkupLine(
                $"{mode}{runlistVerb} {translated.Count} script(s) to [cyan]{Markup.Escape(runlistPath)}[/]");

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
            AnsiConsole.MarkupLine("[bold]db-migrator doctor[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Dependency Injection Configuration:[/]");
            AnsiConsole.MarkupLine($"  [red]{FailIcon}[/] Failed to load startup project: {Markup.Escape(ex.Message)}");
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule().RuleStyle("grey"));
            AnsiConsole.MarkupLine("Summary: [green]0 passed[/], [yellow]0 warnings[/], [red]1 error[/]  (exit code 1)");
            return 1;
        }

        try
        {
            if (settings.RunlistOnly)
            {
                var concreteManager = loaded.Manager;
                IReadOnlyList<AppliedMigrationRecord>? applied = null;
                if (settings.CheckDb)
                {
                    applied = await concreteManager.GetAppliedMigrationsAsync();
                }

                var runlistChecks = concreteManager
                    .CreateRunlistManager()
                    .Validate(applied);

                var mode = settings.CheckDb ? "Live DB" : "Offline";
                AnsiConsole.MarkupLine("[bold]db-migrator doctor --runlist[/]");
                AnsiConsole.MarkupLine($"[grey]Project: {Markup.Escape(loaded.HostName)}  {DotIcon}  Mode: {mode}[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]Migration Runlist:[/]");
                if (runlistChecks.Count == 0)
                {
                    AnsiConsole.MarkupLine($"  [green]{PassIcon}[/] No migration scripts found yet.");
                }
                else
                {
                    RenderItems(runlistChecks);
                }

                var passed = runlistChecks.Count == 0 ? 1 : runlistChecks.Count(c => c.Severity == DoctorSeverity.Pass);
                var warnings = runlistChecks.Count(c => c.Severity == DoctorSeverity.Warning);
                var errors = runlistChecks.Count(c => c.Severity == DoctorSeverity.Error);
                var exit = errors > 0 ? 1 : 0;

                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule().RuleStyle("grey"));
                AnsiConsole.MarkupLine(
                    $"Summary: [green]{passed} passed[/], [yellow]{warnings} {(warnings == 1 ? "warning" : "warnings")}[/], [red]{errors} {(errors == 1 ? "error" : "errors")}[/]  (exit code {exit})");
                return exit;
            }

            var report = await loaded.Manager.DiagnoseAsync(
                settings.RegistryFilter,
                checkDatabase: settings.CheckDb,
                discoveredAssemblies: loaded.LoadedAssemblies);

            var modeLabel = settings.CheckDb ? "Live DB" : "Offline";
            AnsiConsole.MarkupLine("[bold]db-migrator doctor[/]");
            AnsiConsole.MarkupLine(
                $"[grey]Project: {Markup.Escape(loaded.HostName)} ({report.Dialect})  {DotIcon}  Mode: {modeLabel}  {DotIcon}  Registries: {report.RegistryReports.Count}[/]");
            AnsiConsole.WriteLine();

            // Dependency Injection Configuration
            AnsiConsole.MarkupLine("[bold]Dependency Injection Configuration:[/]");
            AnsiConsole.MarkupLine(
                $"  [green]{PassIcon}[/] Services loaded from {Markup.Escape(loaded.StartupProjectDisplayPath)} ({Markup.Escape(loaded.BootstrapSource)})");
            RenderItems(report.WiringChecks);

            // Per-Registry summary
            foreach (var reg in report.RegistryReports)
            {
                AnsiConsole.WriteLine();
                var excludedPart = reg.ExcludedEntityCount > 0
                    ? $" [[{reg.ExcludedEntityCount} excluded]]"
                    : string.Empty;
                AnsiConsole.MarkupLine(
                    $"[bold]Registry: {Markup.Escape(reg.RegistryName)}:[/] [grey]({reg.EntityCount} entities{excludedPart}, {reg.ColumnCount} columns)[/]");
                RenderItems(reg.Items);
            }

            // Cross-Registry Checks
            if (report.CrossRegistryChecks.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]Cross-Registry Checks:[/]");
                RenderItems(report.CrossRegistryChecks);
            }

            // Migration Scripts Coverage
            if (report.MigrationCoverageChecks.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]Migration Scripts:[/]");
                RenderItems(report.MigrationCoverageChecks);
            }

            // Live Database
            if (report.DatabaseChecks.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]Live Database:[/]");
                RenderItems(report.DatabaseChecks);
            }

            var totalPassed = report.PassedCount + 1;
            var exitCode = report.HasErrors ? 1 : 0;

            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule().RuleStyle("grey"));
            AnsiConsole.MarkupLine(
                $"Summary: [green]{totalPassed} passed[/], [yellow]{report.WarningCount} {(report.WarningCount == 1 ? "warning" : "warnings")}[/], [red]{report.ErrorCount} {(report.ErrorCount == 1 ? "error" : "errors")}[/]  (exit code {exitCode})");

            if (!settings.CheckDb)
            {
                AnsiConsole.MarkupLine("[grey]Tip: Pass --check-db to also check the live database.[/]");
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

internal sealed class RunlistShowCommand : AsyncCommand<RunlistShowSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, RunlistShowSettings settings)
    {
        if (settings.Recent is <= 0)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] --recent must be a positive integer.");
            return 1;
        }

        var (owner, manager, _) = TargetAssemblyLoader.CreateManager(settings);
        try
        {
            var runlist = manager.CreateRunlistManager();
            var entries = runlist.ResolveEffectiveOrder();
            if (entries.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]No migration scripts found.[/]");
                return 0;
            }

            var display = settings.Recent is > 0 && settings.Recent.Value < entries.Count
                ? entries.Skip(entries.Count - settings.Recent.Value).ToList()
                : [.. entries];

            var startIndex = entries.Count - display.Count + 1;

            var table = new Table()
                .AddColumn("#")
                .AddColumn("Migration Script ID")
                .AddColumn("Registry")
                .AddColumn("Script")
                .AddColumn("Override");

            for (var i = 0; i < display.Count; i++)
            {
                var e = display[i];
                table.AddRow(
                    (startIndex + i).ToString(),
                    $"[grey]{e.Id:N}[/]",
                    Markup.Escape(e.RegistryName),
                    Markup.Escape(e.ScriptName),
                    e.IsOverridden ? "[yellow]yes[/]" : "[grey]-[/]");
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

internal sealed class RunlistSyncCommand : AsyncCommand<RunlistSyncSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, RunlistSyncSettings settings)
    {
        var (owner, manager, _) = TargetAssemblyLoader.CreateManager(settings);
        try
        {
            var runlist = manager.CreateRunlistManager();
            var result = await runlist.SyncAsync(settings.Prune, settings.DryRun);

            var prefix = settings.DryRun ? "[yellow]DRY RUN:[/] " : string.Empty;
            if (result.StampedFiles is { Count: > 0 })
            {
                var renameVerb = settings.DryRun ? "Would rename" : "Renamed";
                foreach (var stamped in result.StampedFiles)
                {
                    AnsiConsole.MarkupLine($"{prefix}{renameVerb}: [green]{Markup.Escape(stamped)}[/]");
                }
            }

            AnsiConsole.MarkupLine(
                $"{prefix}Synced [cyan]{Markup.Escape(result.RunlistPath)}[/] " +
                $"(stamped: {result.StampedCount}, added: {result.AddedCount}, pruned: {result.PrunedCount}).");

            if (result.StaleOverrideIds is { Count: > 0 })
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]Warning:[/] db-migration-runlist.overrides.json still references {result.StaleOverrideIds.Count} missing script ID(s): " +
                    $"{string.Join(", ", result.StaleOverrideIds)}. Remove them from the overrides file.");
            }

            return 0;
        }
        finally
        {
            await TargetAssemblyLoader.DisposeAsync(owner);
        }
    }
}
