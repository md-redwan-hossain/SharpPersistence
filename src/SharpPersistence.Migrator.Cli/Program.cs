using SharpPersistence.Migrator.Cli;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("db-migrator");

    config.AddCommand<DiffCommand>("diff")
        .WithDescription("Scan entity registries, diff against snapshots, and generate idempotent SQL scripts.")
        .WithExample("diff", "--name", "Initial", "--startup-project", "WebApi/WebApi.csproj");

    config.AddCommand<MigrateCommand>("migrate")
        .WithDescription("Apply all pending SQL migration scripts to the target database.")
        .WithExample("migrate", "--startup-project", "WebApi/WebApi.csproj");

    config.AddCommand<StatusCommand>("status")
        .WithDescription("Show applied, pending, and checksum-mismatched migration scripts.");

    config.AddCommand<PendingCommand>("pending")
        .WithDescription("List pending SQL migration scripts.");

    config.AddCommand<TranslateEfMigrationCommand>("translate-ef-migration")
        .WithDescription("Translate existing EF Core C# migrations in a folder 1-to-1 into SQL scripts.")
        .WithExample("translate-ef-migration", "WebApi/Migrations", "--startup-project", "WebApi/WebApi.csproj");

    config.AddCommand<SyncEfCoreHistoryCommand>("sync-ef-history")
        .WithDescription("Mark _translated scripts already present in __EFMigrationsHistory as applied.");

    config.AddCommand<VerifyCommand>("verify")
        .WithDescription("Compare live database tables and columns against your C# schema to detect drift.")
        .WithExample("verify", "--startup-project", "WebApi/WebApi.csproj");
});

return await app.RunAsync(args);
