---
name: sharp-persistence-migrator
description: >-
  Configure C#-first database migrations, entity registries, fluent table/FK/index/check/exclusion
  builders, compile-time Roslyn entity discovery, EF Core model bridging, DbContext registry binding,
  and db-migrator CLI commands (including config and browser UI) for PostgreSQL and Oracle using
  SharpPersistence.Migrator.
---

# SharpPersistence.Migrator

Use `SharpPersistence.Migrator` (plus `SharpPersistence.Migrator.EfCore` when bridging to `DbContext`) for C#-first schema definitions, idempotent SQL migration scripts, and compile-time entity discovery across PostgreSQL and Oracle.

## DI & Setup (`AddDbMigrator`)

```csharp
// AssemblyInfo.cs (place assembly-level attributes in AssemblyInfo.cs, not Program.cs)
using SharpPersistence.Migrator.Attributes;
[assembly: EntityRegistryRoot]

// Program.cs
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.Generated;

builder.Services.AddDbMigrator(opt =>
{
    // PostgreSQL (defaults: LowerSnakeCase, history table __db_migration_logs in public)
    opt.UsePostgreSql(connectionString)
        .HasPostgresExtension("btree_gist")
        .HasPostgresEnum<BookingStatus>("booking_status");

    // Or Oracle (defaults: UpperSnakeCase, Oracle19)
    // opt.UseOracle(connectionString).UseSqlCompatibility(OracleDbCompatibility.Oracle23);

    opt.UseMigrationsDirectory("MigrationScripts")
        .UseSnapshotDirectory("EntityChangelogs")
        .UseHistoryTable("__db_migration_logs", "public")
        .UseNamingConvention(SqlIdentifierCase.LowerSnakeCase)
        .PluralizeTableNames()
        .UseTableNameConvention((entityType, defaultName) =>
            typeof(ILookupEntity).IsAssignableFrom(entityType) ? $"lk_{defaultName}" : defaultName)
        .ConfigureProperties<string>(c => c.HasMaxLength(200))
        .ConfigureEntities<ICreateTracking>(e =>
            e.HasForeignKey(x => x.CreatedByUserId)
                .References<RefUser>(u => u.Id)
                .OnDelete(DbReferentialAction.Restrict))
        .ConfigureEntityStubs(stubs => stubs.Map<RefUser, User>())
        .ConfigureSqlExpressionBuilder(expr => expr
            .UseDelimitedIdentifiers(true)
            .UseNestedColumnSeparator("_"))
        .AddGeneratedEntityRegistries();
});
```

- Implement `IDesignTimeDbMigratorFactory` in class-library projects without `Program.cs`.
- Inject `IDbMigrator` at runtime (`MigrateAsync`, `GetStatusAsync`, `GetPendingMigrationsAsync`, `GetAppliedMigrationsAsync`, `EnsureHistoryTableAsync`).

## Entity Registries & Fluent Builders

```csharp
[EntityConfiguration<Booking>]
public sealed class BookingConfiguration : IEntityConfiguration<Booking>
{
    public void Configure(EntityBuilder<Booking> builder)
    {
        builder.ToTable("bookings")
            .HasSchema("sales")
            .IsUnlogged()
            .UseTablespace("fast_ssd")
            .HasStorageParameter(PostgresStorageParameter.FillFactor, 85);

        builder.HasKey(x => x.Id).HasName("pk_bookings");
        builder.Property(x => x.Code).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Total)
            .HasPrecision(18, 2)
            .HasComputedColumnSql(expr => expr.Column(x => x.Amount).Math().Plus(x => x.Tax), stored: true);
        builder.Ignore(x => x.ComputedLabel);

        builder.HasForeignKey(x => x.CustomerId)
            .References<Customer>(c => c.Id)
            .HasConstraintName("fk_bookings_customers_customer_id")
            .OnDelete(DbReferentialAction.Restrict);

        // 1-to-1 FK (auto-adds unique index); use .IsCircular() to defer cycles to _deferred/
        builder.HasForeignKey(x => x.InvoiceId)
            .References<Invoice>(i => i.Id)
            .IsOneToOne();

        builder.HasIndex(x => x.CustomerId)
            .HasDatabaseName("ix_bookings_customer_id")
            .HasFilter(expr => expr.Column(x => x.IsCancelled).EqualTo(false))
            .IncludeProperties(x => x.Amount);

        builder.HasCheckConstraint("ck_bookings_amount",
            cc => cc.Column(x => x.Amount).GreaterThan(0m));

        builder.HasExclusionConstraint("excl_bookings_room_slot")
            .HasMethod(PostgresIndexMethod.Gist)
            .WithElement(x => x.RoomId, PostgresExclusionOperator.Equals)
            .WithRange(x => x.StartsAt, x => x.EndsAt, PostgresRangeBounds.InclusiveLowerExclusiveUpper);
    }
}

[EntityRegistry]
public sealed partial class SalesRegistry : IEntityRegistry
{
    public void Configure(IEntityRegistryBuilder registry)
    {
        registry.HasDefaultSchema("sales");
        registry.HasSequence<long>("booking_seq").StartsAt(1000).IncrementsBy(1);
    }
}
```

- Always keep `[EntityRegistry]` classes `sealed partial`, implementing `IEntityRegistry` and `public void Configure(IEntityRegistryBuilder registry)`.
- Always use `HasForeignKey(...).References<TPrincipal>(...)` and `IsOneToOne()` (do not use `HasOne` or `IsUnique()` on FKs).
- Use `OwnsOne(x => x.Nav, a => ...)` for inline table-split owned value objects (supports `Property`, `Ignore`, `HasIndex`, and nested `OwnsOne`; nullable navigations automatically make all owned columns nullable and reject `.IsRequired()`).
- Use `HasDatabaseName(...)` on `IndexBuilder` and `HasName(...)` on `KeyBuilder`.

## Global Conventions & Entity Stubs

- Use `ConfigureEntities<T>` (works with interfaces and base classes) instead of reflection loops for shared FKs, keys, identity columns, and lookup indexes. It runs before `IEntityConfiguration<T>`, so per-entity configs can override a conventional FK or unnamed index without duplicates.
- Use `UseTableNameConvention((entityType, defaultName) => ...)` for custom table prefixes (e.g., `lk_` for lookup entities).
- Point external FKs to local `Ref*` stubs (`[Entity(ReadOnly = true)]` + `References<RefUser>(u => u.Id)`) and wire `ConfigureEntityStubs(stubs => stubs.Map<RefUser, User>())` once in the startup project so registries sort accurately and FKs target the owning table without leaking domain entities across assemblies.

## Compile-Time Discovery Attributes & Diagnostics

- **Domain (`SharpPersistence.Abstractions`):** `[Entity]` (supports `ReadOnly = true` to auto-exclude stub/view entities from migrations), custom `EntityMarkerAttribute` + `[assembly: DiscoverEntitiesBy<TMarker>]`, `[assembly: DisableEntityDiscovery]` (or `<DisableEntityDiscovery>true</DisableEntityDiscovery>`).
- **Migrator (`SharpPersistence.Migrator`):** `[EntityRegistry]`, `[EntityRegistry<TMarker>]`, `[EntityConfiguration<TEntity>]`, `[assembly: EntityRegistryRoot]` (or `<EntityRegistryRoot>true</EntityRegistryRoot>`).
- **Diagnostics:**
  - `SPM001`-`SPM007`: registry/configuration declaration, duplicate config, missing `Configure`, duplicate entity across registries, non-public entity/marker, orphan config warning.
  - `SPM008`-`SPM013`: `[BindEntityRegistry<TRegistry>]` / `[EntityRegistryItem<TEntity>]` validation (partial `DbContext`, valid registry type, duplicate bindings/items, unknown item).
  - `SPM014`: `.IsRequired()` inside an optional `OwnsOne(...)`.
  - `SPM015`: self-mapped or `ReadOnly = true` target in `ConfigureEntityStubs(...)`.

## EF Core Bridge & `DbContext` Registry Binding (`SharpPersistence.Migrator.EfCore`)

- Inject `DatabaseModel` into `DbContext` and call `modelBuilder.ApplySchemaToEfCore(model);` in `OnModelCreating` (or use `[BindEntityRegistry<TRegistry>]` below).
- Decorate a `partial` `DbContext` with `[BindEntityRegistry<TRegistry>]` to generate `DbSet<T>` / `IQueryable<T>` properties and `ApplyBoundRegistries(modelBuilder, model)` at compile time:

```csharp
[BindEntityRegistry<SalesRegistry>]
[BindEntityRegistry<IdentityRegistry>(ReadOnly = true)] // generates IQueryable<T> (.AsNoTracking())
[EntityRegistryItem<AuditLog>(Exclude = true)]
public sealed partial class SalesDbContext(
    DbContextOptions<SalesDbContext> options,
    DatabaseModel model) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplyBoundRegistries(modelBuilder, model);
    }
}
```

- `HasConversion<TProvider>()` and `HasConversion(Type)` are built into `ColumnBuilder` (`SharpPersistence.Migrator`); use `SharpPersistence.Migrator.EfCore`'s `.HasConversion(ValueConverter)` when you need a custom EF Core `ValueConverter` at runtime.

## `db-migrator` CLI Commands

- `db-migrator config --generate` - scans the solution and writes `db-migrator.config.json` (qualifying startup projects, registry projects, and defaults for the searchable interactive project picker; pass `--ci` to disable prompts).
- `db-migrator ui [--port <PORT>] [--no-open]` - launches the local browser UI (Entities explorer, Table ERD, Registry Dependencies & Stubs graph, Migrations Runlist + Dry Run diff, Doctor, and `Ctrl/Cmd+K` search).
- `db-migrator doctor [--registry <Name>] [--check-db] [--runlist]` - validates DI wiring, entity configs, FK types, cross-registry collisions, runlist/override integrity, SQL DDL dependencies, and migration script coverage (read-only).
- `db-migrator generate --name <Name> [--registry <Name>] [--dry-run] [--ci] [--allow-empty]` - generates idempotent SQL scripts stamped with `-- migration-script-id: <uuidv7>` and appends them to `db-migration-runlist.json`.
- `db-migrator runlist show [--recent <N>]` & `db-migrator runlist sync [--prune] [--dry-run]` - inspects the effective execution order (including `db-migration-runlist.overrides.json`) or stamps headers / repairs `db-migration-runlist.json`.
- `db-migrator migrate [--dry-run] [--sync-ef-history]` - applies pending scripts under distributed lock.
- `db-migrator status` / `db-migrator pending` - inspects migration history and pending scripts.
- `db-migrator translate-ef-configs <CONFIGS_DIR> [-o <DIR>] [--dry-run] [--no-attribute]` - converts `IEntityTypeConfiguration<T>` to `IEntityConfiguration<T>`.
- `db-migrator translate-ef-migration <MIGRATIONS_DIR>` & `db-migrator sync-ef-history` - translates EF Core migrations 1-to-1 and syncs `__EFMigrationsHistory`.
