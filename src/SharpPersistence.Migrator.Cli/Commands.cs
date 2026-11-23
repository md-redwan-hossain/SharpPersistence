using System.ComponentModel;
using SharpPersistence.Migrator.EfCore;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SharpPersistence.Migrator.Cli;

public class SharedSettings : CommandSettings
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

public sealed class DiffSettings : SharedSettings
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

public sealed class MigrateSettings : SharedSettings
{
    [CommandOption("--dry-run")]
    [Description("Preview pending scripts without executing against the database.")]
    public bool DryRun { get; init; }

    [CommandOption("--sync-ef-history")]
    [Description("Sync applied entries from __EFMigrationsHistory before running migrations.")]
    public bool SyncEfHistory { get; init; }
}

public sealed class TranslateEfSettings : SharedSettings
{
    [CommandArgument(0, "<MIGRATIONS_DIR>")]
    [Description("Path to the EF Core Migrations folder containing *.cs migration files.")]
    public string MigrationsDirectory { get; init; } = string.Empty;

    [CommandOption("--dry-run")]
    [Description("Preview translated SQL scripts without writing files.")]
    public bool DryRun { get; init; }
}

public sealed class DryRunSettings : SharedSettings
{
    [CommandOption("--dry-run")]
    [Description("Preview without writing changes.")]
    public bool DryRun { get; init; }
}

public sealed class DiffCommand : AsyncCommand<DiffSettings>
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

public sealed class VerifyCommand : AsyncCommand<SharedSettings>
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

public sealed class MigrateCommand : AsyncCommand<MigrateSettings>
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

public sealed class StatusCommand : AsyncCommand<SharedSettings>
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

public sealed class PendingCommand : AsyncCommand<SharedSettings>
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

public sealed class TranslateEfMigrationCommand : AsyncCommand<TranslateEfSettings>
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

public sealed class SyncEfCoreHistoryCommand : AsyncCommand<DryRunSettings>
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
