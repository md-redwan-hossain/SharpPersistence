using SharpPersistence.Migrator.Cli;
using SharpPersistence.Migrator.Cli.Config;
using SharpPersistence.Migrator.Cli.Translators;
using SharpPersistence.Migrator.Cli.Ui;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("db-migrator");

    config.AddCommand<DiffCommand>("generate")
        .WithDescription("Compare entity configurations against your latest migration scripts and generate SQL.")
        .WithExample("generate", "--name", "Initial", "--project", "DbMigrator/DbMigrator.csproj");

    config.AddCommand<MigrateCommand>("migrate")
        .WithDescription("Apply all pending SQL migration scripts to the target database.")
        .WithExample("migrate", "--project", "DbMigrator/DbMigrator.csproj");

    config.AddCommand<StatusCommand>("status")
        .WithDescription("Show applied, pending, and checksum-mismatched migration scripts.");

    config.AddCommand<PendingCommand>("pending")
        .WithDescription("List pending SQL migration scripts.");

    config.AddCommand<TranslateEfMigrationCommand>("translate-ef-migrations")
        .WithDescription("Translate existing EF Core C# migrations in a folder 1-to-1 into SQL scripts.")
        .WithExample("translate-ef-migrations", "--source-path", "WebApi/Migrations", "--project", "DbMigrator/DbMigrator.csproj");

    config.AddCommand<TranslateEfConfigsCommand>("translate-ef-configs")
        .WithDescription("Translate EF Core IEntityTypeConfiguration<T> C# files into SharpPersistence.Migrator IEntityConfiguration<T> files.")
        .WithExample("translate-ef-configs", "--source-path", "Infrastructure/Configurations", "--dry-run")
        .WithExample("translate-ef-configs", "--source-path", "Infrastructure/Configurations", "--output-path", "Infrastructure/MigratorConfigs");

    config.AddCommand<AnnotateEntitiesCommand>("annotate-entities")
        .WithDescription("Annotate entity classes in a directory with a SharpPersistence entity marker attribute.")
        .WithExample("annotate-entities", "--directory", "Domain/Entities", "--dry-run");

    config.AddCommand<SyncEfCoreHistoryCommand>("sync-ef-history")
        .WithDescription("Mark _translated scripts already present in __EFMigrationsHistory as applied.");

    config.AddBranch("runlist", runlist =>
    {
        runlist.SetDescription("Inspect or sync db-migration-runlist.json and -- migration-script-id headers.");
        runlist.AddCommand<RunlistShowCommand>("show")
            .WithDescription("Print the effective migration execution order (after applying overrides).")
            .WithExample("runlist", "show", "--project", "DbMigrator/DbMigrator.csproj");
        runlist.AddCommand<RunlistSyncCommand>("sync")
            .WithDescription("Stamp missing -- migration-script-id headers and generate/update db-migration-runlist.json.")
            .WithExample("runlist", "sync", "--project", "DbMigrator/DbMigrator.csproj");
    });

    config.AddCommand<DoctorCommand>("doctor")
        .WithDescription("Validate DI wiring, entity registries, configurations, migration script coverage, and optionally live database connectivity.")
        .WithExample("doctor", "--project", "DbMigrator/DbMigrator.csproj")
        .WithExample("doctor", "--registry", "SalesRegistry", "--check-db");

    config.AddCommand<ConfigCommand>("config")
        .WithDescription("Generate, inspect, or update solution-level db-migrator-config.json.")
        .WithExample("config", "--generate")
        .WithExample("config", "--set-default", "src/DbMigrator/DbMigrator.csproj")
        .WithExample("config", "--show");

    config.AddCommand<UiCommand>("ui")
        .WithDescription("Launch an interactive browser UI for exploring entities, DB metadata, ERD, runlist, and doctor checks.")
        .WithExample("ui")
        .WithExample("ui", "--port", "5077");
});

return await app.RunAsync(args);
