using System.ComponentModel;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SharpPersistence.Migrator.Cli.Ui;

internal sealed class UiSettings : SharedSettings
{
    [CommandOption("-p|--port <PORT>")]
    [Description("Local port to serve the UI on (default: 5077).")]
    [DefaultValue(5077)]
    public int Port { get; init; } = 5077;

    [CommandOption("--no-open")]
    [Description("Do not automatically open the browser.")]
    public bool NoOpen { get; init; }

    [CommandOption("--export <FILE>")]
    [Description("Export a standalone offline HTML report with embedded schema, runlist, and doctor snapshot.")]
    public string? ExportPath { get; init; }

    [CommandOption("--check-db")]
    [Description("Include live database connectivity and drift checks in the initial doctor snapshot.")]
    public bool CheckDb { get; init; }

    public override ValidationResult Validate() =>
        Port is < 1 or > 65535
            ? ValidationResult.Error("--port must be between 1 and 65535.")
            : ValidationResult.Success();
}

internal sealed class UiCommand : AsyncCommand<UiSettings>
{
    private const string EmbeddedHtmlResourceName = "SharpPersistence.Migrator.Cli.Ui.index.html";

    public override async Task<int> ExecuteAsync(CommandContext context, UiSettings settings)
    {
        var loaded = TargetAssemblyLoader.CreateManagerContext(settings);
        try
        {
            var htmlTemplate = LoadEmbeddedHtml();

            if (!string.IsNullOrWhiteSpace(settings.ExportPath))
            {
                var exportSnapshot = await UiSnapshotBuilder.BuildSnapshotAsync(
                    loaded,
                    checkDatabase: settings.CheckDb,
                    includeScriptSql: true,
                    ct: CancellationToken.None);

                var outputHtml = UiSnapshotBuilder.InjectSnapshotIntoHtml(htmlTemplate, exportSnapshot);
                var fullExportPath = Path.GetFullPath(settings.ExportPath);
                var dir = Path.GetDirectoryName(fullExportPath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                await File.WriteAllTextAsync(fullExportPath, outputHtml);
                AnsiConsole.MarkupLine($"[green]Exported UI snapshot[/] → [cyan]{Markup.Escape(fullExportPath)}[/]");
                return 0;
            }

            var initialSnapshot = await UiSnapshotBuilder.BuildSnapshotAsync(
                loaded,
                checkDatabase: settings.CheckDb,
                includeScriptSql: false,
                ct: CancellationToken.None);

            var url = $"http://127.0.0.1:{settings.Port}";
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls(url);
            builder.Logging.ClearProviders();

            var app = builder.Build();

            app.MapGet("/", () => Results.Content(htmlTemplate, "text/html; charset=utf-8"));

            app.MapGet("/api/snapshot", () =>
                Results.Json(initialSnapshot, UiSnapshotBuilder.JsonOptions));

            app.MapGet("/api/doctor", async (bool? checkDb, CancellationToken ct) =>
            {
                var doctor = await UiSnapshotBuilder.BuildDoctorAsync(loaded, checkDb ?? false, ct);
                return Results.Json(doctor, UiSnapshotBuilder.JsonOptions);
            });

            app.MapGet("/api/runlist/{id}/sql", (string id) =>
            {
                var sql = UiSnapshotBuilder.ReadRunlistScriptSql(loaded.Manager, id);
                return sql is null
                    ? Results.NotFound(new { error = $"Migration script '{id}' not found." })
                    : Results.Content(sql, "text/plain; charset=utf-8");
            });

            AnsiConsole.MarkupLine($"[bold]db-migrator ui[/] [grey]({Markup.Escape(loaded.HostName)} · {loaded.Manager.Options.Dialect})[/]");
            AnsiConsole.MarkupLine($"  [green]●[/] Serving at [link={url}][cyan]{url}[/][/]");
            AnsiConsole.MarkupLine("  [grey]Press Ctrl+C to stop.[/]");

            if (!settings.NoOpen)
            {
                TryOpenBrowser(url);
            }

            await app.RunAsync();
            return 0;
        }
        finally
        {
            await TargetAssemblyLoader.DisposeAsync(loaded.ServiceScopeOwner);
        }
    }

    internal static string LoadEmbeddedHtml()
    {
        using var stream = typeof(UiCommand).Assembly.GetManifestResourceStream(EmbeddedHtmlResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded UI asset '{EmbeddedHtmlResourceName}' was not found. Build the ui/ workspace with 'npm run build'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Headless environments or restricted shells: user can open the printed URL manually
        }
    }
}
