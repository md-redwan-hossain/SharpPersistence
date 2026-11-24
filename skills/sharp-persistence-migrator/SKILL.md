---
name: sharp-persistence-migrator
description: >-
  Configure C#-first database migrations, entity registries, fluent table/FK/index/check/exclusion
  builders, compile-time Roslyn entity discovery, EF Core model bridging, and db-migrator CLI commands
  for PostgreSQL and Oracle using SharpPersistence.Migrator.
---

# SharpPersistence.Migrator

Use `SharpPersistence.Migrator` (plus `SharpPersistence.Migrator.EfCore` when bridging to `DbContext`) for C#-first schema definitions, idempotent SQL migration scripts, and compile-time entity discovery across PostgreSQL and Oracle.

## DI & Setup (`AddDbMigrator`)

```csharp
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Attributes;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.Generated;

[assembly: EntityRegistryRoot]

builder.Services.AddDbMigrator(opt =>
{
    // PostgreSQL (defaults: LowerSnakeCase, history table __db_migration_logs in public)
    opt.UsePostgreSql(connectionString)
        .HasPostgresExtension("btree_gist")
        .HasPostgresEnum<BookingStatus>("booking_status");

    // Or Oracle (defaults: UpperSnakeCase, OracleDb19)
    // opt.UseOracle(connectionString).UseSqlCompatibility(OracleDbCompatibility.OracleDb23);

    opt.UseMigrationsDirectory("MigrationScripts")
        .UseSnapshotDirectory("EntityChangelogs")
        .UseHistoryTable("__db_migration_logs", "public")
        .ConfigureProperties<string>(c => c.HasMaxLength(200))
        .ConfigureCheckConstraintBuilder(cc => cc
            .UseDelimitedIdentifiers(true)
            .UseNestedColumnSeparator("_"))
        .AddGeneratedEntityRegistries();
});
```

- Implement `IDesignTimeMigrationFactory` in class-library projects without `Program.cs`.
- Inject `IMigrationManager` at runtime (`MigrateAsync`, `GetStatusAsync`, `GetPendingMigrationsAsync`, `GetAppliedMigrationsAsync`, `EnsureHistoryTableAsync`).

## Entity Registries & Fluent Builders

```csharp
[EntityConfiguration<Booking>]
public sealed class BookingConfiguration : IEntityConfiguration<Booking>
{
    public void Configure(EntityBuilder<Booking> builder)
    {
        builder.ToTable("bookings", "sales")
            .IsUnlogged()
            .UseTablespace("fast_ssd")
            .HasStorageParameter(PostgresStorageParameter.FillFactor, 85);

        builder.HasKey(x => x.Id).HasName("pk_bookings");
        builder.Property(x => x.Code).HasMaxLength(32).IsRequired();
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
            .HasFilter(cc => cc.Column(x => x.IsCancelled).EqualTo(false))
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
- Use `HasDatabaseName(...)` on `IndexBuilder` and `HasName(...)` on `KeyBuilder`.

## Compile-Time Discovery Attributes & Diagnostics

- **Domain (`SharpPersistence.Abstractions`):** `[Entity]`, custom `EntityMarkerAttribute` + `[assembly: DiscoverEntitiesBy<TMarker>]`, `[assembly: DisableEntityDiscovery]` (or `<DisableEntityDiscovery>true</DisableEntityDiscovery>`).
- **Migrator (`SharpPersistence.Migrator`):** `[EntityRegistry]`, `[EntityRegistry<TMarker>]`, `[EntityConfiguration<TEntity>]`, `[assembly: EntityRegistryRoot]` (or `<EntityRegistryRoot>true</EntityRegistryRoot>`).
- **Diagnostics:** `SPM001` (sealed partial `IEntityRegistry`), `SPM002` (valid `IEntityConfiguration<T>`), `SPM003` (duplicate config), `SPM004` (missing `public void Configure`), `SPM005` (entity matched by multiple registries), `SPM006` (non-public entity/marker), `SPM007` (orphan config warning).

## EF Core Bridge (`SharpPersistence.Migrator.EfCore`)

- Inject `DatabaseSchema` into `DbContext` and call `modelBuilder.ApplySchemaToEfCore(schema);` in `OnModelCreating`.
- `HasConversion<TProvider>()` and `HasConversion(Type)` are built into `ColumnBuilder` (`SharpPersistence.Migrator`); use `SharpPersistence.Migrator.EfCore`'s `.HasConversion(ValueConverter)` when you need a custom EF Core `ValueConverter` at runtime.

## `db-migrator` CLI Commands

- `db-migrator doctor [--registry <Name>] [--check-db]` — validates DI wiring, entity configs, FK types, cross-registry collisions, and migration script coverage (read-only).
- `db-migrator diff --name <Name> [--registry <Name>] [--dry-run] [--ci] [--allow-empty]` — generates idempotent SQL scripts.
- `db-migrator migrate [--dry-run] [--sync-ef-history]` — applies pending scripts under distributed lock.
- `db-migrator status` / `db-migrator pending` / `db-migrator verify` — inspects history & live DB sync.
- `db-migrator translate-ef-configs <CONFIGS_DIR> [-o <DIR>] [--dry-run] [--no-attribute]` — converts `IEntityTypeConfiguration<T>` to `IEntityConfiguration<T>`.
- `db-migrator translate-ef-migration <MIGRATIONS_DIR>` & `db-migrator sync-ef-history` — translates EF Core migrations 1-to-1 and syncs `__EFMigrationsHistory`.
