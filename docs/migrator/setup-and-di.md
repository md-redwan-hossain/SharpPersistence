# Setup, DI & Runtime Execution

`SharpPersistence.Migrator` is configured once in your startup project through `AddDbMigrator(...)`, which registers `IDbMigrator` so both your application and the `db-migrator` CLI share the exact same configuration.

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

    opt.UseHistoryTable("__db_migration_logs", schema: "public")
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
        .UseSqlCompatibility(OracleDbCompatibility.Oracle23);

    opt.UseNamingConvention(SqlIdentifierCase.UpperSnakeCase)
        .PluralizeTableNames()
        .AddGeneratedEntityRegistries();
});
```

:::

## All `DbMigratorOptionsBuilder` Options

| Method | Default | Purpose |
| --- | --- | --- |
| `UsePostgreSql(connStr / factory)` | — | Sets dialect to `PostgreSql` and identifier case to `LowerSnakeCase` |
| `UseOracle(connStr / factory)` | — | Sets dialect to `Oracle` and identifier case to `UpperSnakeCase` |
| `UseNamingConvention(case)` | Dialect default | `LowerSnakeCase`, `UpperSnakeCase`, `PascalCase`, `CamelCase`, `LowerCase`, or `UpperCase` |
| `PluralizeTableNames(bool)` | `false` | Pluralizes convention-derived table names (`Order` → `orders`, `OrderCategory` → `order_categories`) |
| `UseHistoryTable(name, schema?)` | `__db_migration_logs` | Customizes the applied-migrations tracking table |
| `UseTableNameConvention(fn)` | — | Customizes convention-derived table names (`(entityType, defaultName) => ...`) |
| `UseRootPath(path)` | Working dir | Base directory for `DbMigrator/Scripts` and `DbMigrator/Snapshots` |
| `ConfigureProperties<T>(action)` | — | Global CLR-type conventions (e.g. all `string` max length `255`, or `Enum`) |
| `ConfigureEntities<T>(action)` | — | Global entity/interface conventions (e.g. shared audit FKs, keys, schemas) |
| `ConfigureEntityStubs(cfg)` | — | Maps cross-module `Ref*` stub types to their owning entity (`stubs.Map<RefUser, User>()`) |
| `ConfigureSqlExpressionBuilder(cfg)` | `DelimitIdentifiers = true`, `NestedColumnSeparator = "_"` | Global defaults for typed `HasCheckConstraint`, `HasFilter`, and `HasComputedColumnSql` |
| `AddRegistry<T>()` / `AddRegistry(reg)` | — | Manually register an `IEntityRegistry` (prefer `AddGeneratedEntityRegistries()`) |

## Global Property Conventions

Apply defaults across every entity by CLR type (including `typeof(Enum)` for all enums):

```csharp
opt.ConfigureProperties<string>(c => c.HasMaxLength(200))
   .ConfigureProperties<decimal>(c => c.HasPrecision(18, 4))
   .ConfigureProperties<Enum>(c => c.HasMaxLength(50));
```

## Global Entity Conventions & Cross-Module Stubs

Apply shared foreign keys, primary keys, lookup indexes, or table-naming rules across interfaces and base classes without reflection—and map `Ref*` stubs in your startup project so modules stay isolated:

```csharp
opt.UseTableNameConvention((entityType, defaultName) =>
    typeof(ILookupEntity).IsAssignableFrom(entityType) ? $"lk_{defaultName}" : defaultName);

opt.ConfigureEntities<ICreateTracking>(e =>
    e.HasForeignKey(x => x.CreatedByUserId)
        .References<RefUser>(u => u.Id)
        .OnDelete(DbReferentialAction.Restrict));

opt.ConfigureEntityStubs(stubs => stubs
    .Map<RefUser, User>()
    .Map<RefCompany, Company>());
```

See [Global Conventions & Cross-Module Stubs](/migrator/conventions-and-stubs) for full examples covering audit/bookkeeping contracts, lookup table policies, per-entity overrides, and modular-monolith wiring.

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

If your migrations live in a class library without `Program.cs`, implement `IDesignTimeDbMigratorFactory`:

```csharp
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Configuration;

public sealed class DesignTimeDbMigratorFactory : IDesignTimeDbMigratorFactory
{
    public void Configure(DbMigratorOptionsBuilder options, string[] args)
    {
        options.UsePostgreSql("Host=localhost;Database=app_dev;Username=postgres;Password=postgres")
            .AddRegistry<AppEntityRegistry>();
    }
}
```

## Runtime `IDbMigrator`

Inject `IDbMigrator` to check status or apply migrations on startup:

```csharp
using SharpPersistence.Migrator;
using SharpPersistence.Migrator.Abstractions;

await using var scope = app.Services.CreateAsyncScope();
var migrator = scope.ServiceProvider.GetRequiredService<IDbMigrator>();

if (DbMigratorState.IsDesignTime)
{
    return;
}

if (!await migrator.CanConnectToDatabaseAsync(cancellationToken))
{
    return;
}

if (await migrator.HasPendingMigrationAsync(cancellationToken))
{
    await migrator.MigrateAsync(cancellationToken: cancellationToken);
}
```

`MigrateAsync` automatically acquires a distributed lock (`pg_advisory_lock` on PostgreSQL, `DBMS_LOCK` on Oracle) so multiple app instances starting concurrently never race.

## EF Core `DbContext` Binding (`[BindEntityRegistry<TRegistry>]`)

With `SharpPersistence.Migrator.EfCore`, bind one or more `[EntityRegistry]` classes directly to a `sealed partial` `DbContext` to auto-generate `DbSet<T>` / `IQueryable<T>` (`AsNoTracking`) properties, constructor, and scoped `OnModelCreating`:

```csharp
using Microsoft.EntityFrameworkCore;
using SharpPersistence.Migrator.EfCore.Attributes;

[BindEntityRegistry<BillingRegistry>]
[BindEntityRegistry<LookupDataRegistry>(ReadOnly = true)]
[EntityRegistryItem<AuditLog>(ReadOnly = true)]
[EntityRegistryItem<InventoryData>(Pluralize = false)]
[EntityRegistryItem<OutboxMessage>(Exclude = true)]
public sealed partial class BillingDbContext : DbContext
{
    // Manual properties for an entity automatically suppress duplicate generation
    public DbSet<PortfolioProduct> ProductPortfolios => Set<PortfolioProduct>();

    // Optional hook called at the end of generated OnModelCreating
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pg_trgm");
    }
}
```

- Entities decorated with `[Entity(ReadOnly = true)]` or `[Entity<TGroup>(ReadOnly = true)]` automatically get `ExcludeFromMigrations = true` in `SchemaScanner` and generate `IQueryable<T> => Set<T>().AsNoTracking();` on bound `DbContext` classes.
- If you declare your own constructor or `OnModelCreating`, the generator automatically skips emitting its own (and emits `SPM013` if `DatabaseModel` / `ApplySchemaToEfCore` does not appear wired).
- Compile-time rules `SPM008`–`SPM013` validate `sealed partial DbContext`, mutually exclusive `Exclude = true` with `ReadOnly`/`Pluralize`, duplicates, unmatched `[EntityRegistryItem<T>]`, property name collisions, and `ApplySchemaToEfCore` wiring.
