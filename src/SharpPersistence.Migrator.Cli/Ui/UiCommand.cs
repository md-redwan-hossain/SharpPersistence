using System.ComponentModel;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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
            var dataService = new UiDataService(loaded);
            var overview = TargetAssemblyLoader.RunWithStatus(
                TargetAssemblyLoader.CreateStderrConsole(),
                "Scanning registries & building UI overview...",
                () => dataService.BuildOverview());

            var url = $"http://127.0.0.1:{settings.Port}";
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls(url);
            builder.Logging.ClearProviders();
            builder.Services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
            });

            var app = builder.Build();
            app.UseResponseCompression();

            app.MapGet("/", () => Results.Content(htmlTemplate, "text/html; charset=utf-8"));

            app.MapGet("/api/overview", () =>
                Results.Json(overview, UiDataService.JsonOptions));

            app.MapGet("/api/tables/{registry}/{entity}", (string registry, string entity) =>
            {
                var detail = dataService.GetTableDetail(
                    Uri.UnescapeDataString(registry),
                    Uri.UnescapeDataString(entity));

                return detail is null
                    ? Results.NotFound(new { error = $"Entity '{entity}' in registry '{registry}' not found." })
                    : Results.Json(detail, UiDataService.JsonOptions);
            });

            app.MapGet("/api/erd", (string? registry) =>
                Results.Json(dataService.BuildErd(registry), UiDataService.JsonOptions));

            app.MapGet("/api/dependencies", () =>
                Results.Json(dataService.BuildDependencies(), UiDataService.JsonOptions));

            app.MapGet("/api/migrations/dry-run", () =>
                Results.Json(dataService.BuildDryRun(), UiDataService.JsonOptions));

            app.MapGet("/api/search", (string? q, int? limit) =>
                Results.Json(dataService.SearchSchema(q, limit ?? 40), UiDataService.JsonOptions));

            app.MapGet("/api/runlist", () =>
                Results.Json(dataService.BuildRunlist(), UiDataService.JsonOptions));

            app.MapGet("/api/runlist/{id}/sql", (string id) =>
            {
                var sql = dataService.ReadRunlistScriptSql(id);
                return sql is null
                    ? Results.NotFound(new { error = $"Migration script '{id}' not found." })
                    : Results.Content(sql, "text/plain; charset=utf-8");
            });

            app.MapGet("/api/runlist/{id}/ddls", (string id) =>
            {
                var sql = dataService.ReadRunlistScriptSql(id);
                if (sql is null)
                    return Results.NotFound(new { error = $"Migration script '{id}' not found." });

                var stmts = UiDdlCatalogChecker.ParseScriptDdls(sql, loaded.Manager.Options.Dialect);
                return Results.Json(new UiScriptDdlsDto(id, stmts), UiDataService.JsonOptions);
            });

            app.MapGet("/api/runlist/{id}/check-live", async (string id, CancellationToken ct) =>
            {
                var sql = dataService.ReadRunlistScriptSql(id);
                if (sql is null)
                    return Results.NotFound(new { error = $"Migration script '{id}' not found." });

                var result = await UiDdlCatalogChecker.CheckScriptAsync(loaded.Manager.Options, loaded.Manager.ConnectionOpener, id, sql, ct);
                return Results.Json(result, UiDataService.JsonOptions);
            });

            app.MapPost("/api/ddl/check-live", async (HttpRequest httpRequest, CancellationToken ct) =>
            {
                var req = await httpRequest.ReadFromJsonAsync<UiDdlCheckRequestDto>(UiDataService.JsonOptions, ct);
                if (req is null)
                    return Results.BadRequest(new { error = "Invalid DDL check request." });

                var result = await UiDdlCatalogChecker.CheckSingleAsync(loaded.Manager.Options, loaded.Manager.ConnectionOpener, req, ct);
                return Results.Json(result, UiDataService.JsonOptions);
            });

            app.MapGet("/api/doctor", async (bool? checkDb, CancellationToken ct) =>
            {
                var doctor = await dataService.BuildDoctorAsync(checkDb ?? false, ct);
                return Results.Json(doctor, UiDataService.JsonOptions);
            });

            AnsiConsole.MarkupLine(
                $"[bold]db-migrator ui[/] [grey]({Markup.Escape(loaded.HostName)} \u00b7 {loaded.Manager.Options.Dialect} \u00b7 {overview.TotalEntities} entities)[/]");
            AnsiConsole.MarkupLine($"  [green]\u25cf[/] Serving at [link={url}][cyan]{url}[/][/]");
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
        using var stream = typeof(UiCommand).Assembly.GetManifestResourceStream(EmbeddedHtmlResourceName);
        if (stream is null)
        {
            return """
                <!doctype html>
                <html lang="en">
                <head><meta charset="utf-8" /><title>SharpPersistence Migrator UI</title></head>
                <body style="font-family:system-ui,sans-serif;padding:2rem;line-height:1.5">
                  <h1>UI bundle not embedded</h1>
                  <p>Run the Vite dev server in <code>ui/</code> (<code>npm run dev</code>) and open <code>http://localhost:5173</code>, or build the UI bundle with <code>npm run build</code>.</p>
                </body>
                </html>
                """;
        }

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
            // Headless environments or restricted shells
        }
    }
}
