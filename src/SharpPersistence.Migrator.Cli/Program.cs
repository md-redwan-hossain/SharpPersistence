using SharpPersistence.Migrator.Cli;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("db-migrator");

    config.AddCommand<DiffCommand>("diff")
        .WithDescription("Compare entity configurations against your latest migration scripts and generate SQL.")
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

    config.AddCommand<TranslateEfConfigsCommand>("translate-ef-configs")
        .WithDescription("Translate EF Core IEntityTypeConfiguration<T> C# files into SharpPersistence.Migrator IEntityConfiguration<T> files.")
        .WithExample("translate-ef-configs", "Infrastructure/Configurations", "--output-dir", "Infrastructure/MigratorConfigs", "--dry-run");

    config.AddCommand<SyncEfCoreHistoryCommand>("sync-ef-history")
        .WithDescription("Mark _translated scripts already present in __EFMigrationsHistory as applied.");

    config.AddCommand<VerifyCommand>("verify")
        .WithDescription("Compare your live database against your entity configurations to find unexpected differences.")
        .WithExample("verify", "--startup-project", "WebApi/WebApi.csproj");

    config.AddCommand<DoctorCommand>("doctor")
        .WithDescription("Validate DI wiring, entity registries, configurations, migration script coverage, and optionally live database connectivity.")
        .WithExample("doctor", "--startup-project", "WebApi/WebApi.csproj")
        .WithExample("doctor", "--registry", "SalesRegistry", "--check-db");
});

return await app.RunAsync(args);
