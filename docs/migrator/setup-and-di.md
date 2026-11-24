# Setup, DI & Runtime Execution

`SharpPersistence.Migrator` is configured once in your startup project through `AddDbMigrator(...)`, which registers `IMigrationManager` so both your application and the `db-migrator` CLI share the exact same configuration.

> **Prerequisite:** Add `[assembly: EntityRegistryRoot]` to your startup project's `AssemblyInfo.cs` so `.AddGeneratedEntityRegistries()` is generated for you.

## Basic DI Setup

::: code-group

```csharp [PostgreSQL]
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Generated;

builder.Services.AddDbMigrator(opt =>
{
    opt.UsePostgreSql(builder.Configuration.GetConnectionString("Default")!)
        .HasPostgresExtension("pg_trgm")
        .HasPostgresEnum<OrderStatus>("order_status");

    opt.UseMigrationsDirectory("MigrationScripts")
        .UseSnapshotDirectory("EntityChangelogs")
        .UseHistoryTable("__db_migration_logs", schema: "public")
        .AddGeneratedEntityRegistries();
});
```

```csharp [Oracle]
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.Generated;

builder.Services.AddDbMigrator(opt =>
{
    opt.UseOracle(builder.Configuration.GetConnectionString("Oracle")!)
        .UseSqlCompatibility(OracleDbCompatibility.OracleDb23);

    opt.UseNamingConvention(SqlIdentifierCase.UpperSnakeCase)
        .PluralizeTableNames()
        .AddGeneratedEntityRegistries();
});
```

:::

## All `MigrationManagerOptionsBuilder` Options

| Method | Default | Purpose |
| --- | --- | --- |
| `UsePostgreSql(connStr / factory)` | — | Sets dialect to `PostgreSql` and identifier case to `LowerSnakeCase` |
| `UseOracle(connStr / factory)` | — | Sets dialect to `Oracle` and identifier case to `UpperSnakeCase` |
| `UseNamingConvention(case)` | Dialect default | `LowerSnakeCase`, `UpperSnakeCase`, `PascalCase`, `CamelCase`, `LowerCase`, or `UpperCase` |
| `PluralizeTableNames(bool)` | `false` | Pluralizes convention-derived table names (`Order` → `orders`, `OrderCategory` → `order_categories`) |
| `UseHistoryTable(name, schema?)` | `__db_migration_logs` | Customizes the applied-migrations tracking table |
| `UseMigrationsDirectory(dir)` | `MigrationScripts` | Output folder for generated `.sql` scripts |
| `UseSnapshotDirectory(dir)` | `EntityChangelogs` | Output folder for schema snapshot `.json` files |
| `UseRootPath(path)` | Working dir | Base directory for relative migration/snapshot paths |
| `ConfigureProperties<T>(action)` | — | Global CLR-type conventions (e.g. all `string` max length `255`, or `Enum`) |
| `ConfigureSqlExpressionBuilder(cfg)` | `DelimitIdentifiers = true`, `NestedColumnSeparator = "_"` | Global defaults for typed `HasCheckConstraint`, `HasFilter`, and `HasComputedColumnSql` |
| `AddRegistry<T>()` / `AddRegistry(reg)` | — | Manually register an `IEntityRegistry` |
| `AddRegistriesFromAssembly(...)` | — | Reflection-based registry scan (prefer `AddGeneratedEntityRegistries()`) |

## Global Property Conventions

Apply defaults across every entity by CLR type (including `typeof(Enum)` for all enums):

```csharp
opt.ConfigureProperties<string>(c => c.HasMaxLength(200))
   .ConfigureProperties<decimal>(c => c.HasPrecision(18, 4))
   .ConfigureProperties<Enum>(c => c.HasMaxLength(50));
```

## Custom Connection Factory

If you manage `DbDataSource` or `DbConnection` creation in DI, use the `(sp, opt)` overload:

```csharp
builder.Services.AddDbMigrator((sp, opt) =>
{
    var dataSource = sp.GetRequiredService<NpgsqlDataSource>();
    opt.UsePostgreSql(_ => dataSource.OpenConnection())
        .AddGeneratedEntityRegistries();
});
```

## Design-Time Factory (Class Libraries)

If your migrations live in a class library without `Program.cs`, implement `IDesignTimeMigrationFactory`:

```csharp
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Configuration;

public sealed class DesignTimeMigrationFactory : IDesignTimeMigrationFactory
{
    public void Configure(MigrationManagerOptionsBuilder options, string[] args)
    {
        options.UsePostgreSql("Host=localhost;Database=app_dev;Username=postgres;Password=postgres")
            .AddRegistry<AppEntityRegistry>();
    }
}
```

## Runtime `IMigrationManager`

Inject `IMigrationManager` to check status or apply migrations on startup:

```csharp
using SharpPersistence.Migrator.Abstractions;

await using var scope = app.Services.CreateAsyncScope();
var migrator = scope.ServiceProvider.GetRequiredService<IMigrationManager>();

IReadOnlyList<string> pending = await migrator.GetPendingMigrationsAsync(ct);
if (pending.Count > 0)
{
    await migrator.MigrateAsync(ct: ct);
}
```

`MigrateAsync` automatically acquires a distributed lock (`pg_advisory_lock` on PostgreSQL, `DBMS_LOCK` on Oracle) so multiple app instances starting concurrently never race.
