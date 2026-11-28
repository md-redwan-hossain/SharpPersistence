# Entities and Configurations

`SharpPersistence.Migrator` splits table setup into three focused pieces—wired together automatically at compile time with zero startup reflection.

## The Three Building Blocks

* **Entity (`[Entity]` or `[Entity<TGroup>]`)** — Your plain domain class. Marking it tells the compiler it maps to a database table without pulling EF Core or migration packages into your Domain project.
* **Entity Configuration (`[EntityConfiguration<T>]` + `IEntityConfiguration<T>`)** — Holds one table's mapping rules (table name, columns, keys, foreign keys, indexes) so domain classes stay clean. Entities without one still map by convention (`Id` as primary key).
* **Entity Registry (`[EntityRegistry]` + `IEntityRegistry`)** — Groups related tables into a module/schema, defines sequences, and lets `db-migrator` order or target registries (`--registry SalesRegistry`).

## How They Connect

1. **Domain:** Mark entities with `[Entity]` (ungrouped) or `[Entity<TGroup>]` (with a `readonly record struct ... : IEntityGroup` tag).
2. **Infrastructure:** Add `[EntityConfiguration<T>]` classes and a `sealed partial` `[EntityRegistry]` class.
3. **Startup (`AssemblyInfo.cs`):** Add `[assembly: EntityRegistryRoot]` in `AssemblyInfo.cs`, then call `.AddGeneratedEntityRegistries()` in `Program.cs`.
4. **Build & Run:** The source generator pairs every entity with its config, wires your registries, and catches mistakes (`SPM001`–`SPM007`) at compile time.

::: tip Put Assembly Attributes in `AssemblyInfo.cs`
Keep `[assembly: EntityRegistryRoot]` and `[assembly: DisableEntityDiscovery]` in each project's `AssemblyInfo.cs` so they are easy to find instead of buried in `Program.cs` or entity files.
:::

## Single-Project vs. Modular Monolith

::: code-group

```csharp [Single Project]
// AssemblyInfo.cs
[assembly: EntityRegistryRoot]

// Domain
[Entity]
public sealed class TodoItem
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
}

// Infrastructure
[EntityConfiguration<TodoItem>]
public sealed class TodoItemConfiguration : IEntityConfiguration<TodoItem>
{
    public void Configure(EntityBuilder<TodoItem> builder) => builder.ToTable("todos");
}

[EntityRegistry]
public sealed partial class AppRegistry : IEntityRegistry
{
    public void Configure(IEntityRegistryBuilder registry) => registry.HasDefaultSchema("public");
}
```

```csharp [Modular Monolith]
// Billing.Domain/BillingGroup.cs
public readonly record struct BillingGroup : IEntityGroup;

// Billing.Domain/Invoice.cs
[Entity<BillingGroup>]
public sealed class Invoice { public Guid Id { get; set; } }

// Billing.Infrastructure/BillingRegistry.cs
[EntityRegistry<BillingGroup>]
public sealed partial class BillingRegistry : IEntityRegistry
{
    public void Configure(IEntityRegistryBuilder registry) => registry.HasDefaultSchema("billing");
}

// App.Api/AssemblyInfo.cs
[assembly: EntityRegistryRoot]
```

:::

## `AssemblyInfo.cs` vs. `.csproj` Options

| Goal | `AssemblyInfo.cs` *(Recommended)* | `.csproj` Property |
| --- | --- | --- |
| Mark startup project | `[assembly: EntityRegistryRoot]` | `<EntityRegistryRoot>true</EntityRegistryRoot>` |
| Disable discovery | `[assembly: DisableEntityDiscovery]` | `<DisableEntityDiscovery>true</DisableEntityDiscovery>` |

## Build-Time Diagnostics (`SPM001`–`SPM007`)

| Code | Severity | Meaning |
| --- | --- | --- |
| `SPM001` | Error | `[EntityRegistry]` class (and enclosing types) must be `sealed`, `partial`, and implement `IEntityRegistry` |
| `SPM002` | Error | `[EntityConfiguration<T>]` class must be `sealed`, have a public parameterless constructor, and implement `IEntityConfiguration<T>` |
| `SPM003` | Error | Multiple `[EntityConfiguration<T>]` classes target the same `TEntity` |
| `SPM004` | Error | `[EntityRegistry]` class must declare `public void Configure(IEntityRegistryBuilder registry)` |
| `SPM005` | Error | Same entity is matched by multiple `[EntityRegistry]` classes in the same project |
| `SPM006` | Error | Discovered entity or `IEntityGroup` struct must be `public` |
| `SPM007` | Warning | `[EntityConfiguration<T>]` is not matched by any `[EntityRegistry]` in this project |
| `SPM014` | Error | `.IsRequired()` cannot be called inside an optional `OwnsOne(...)` navigation |
| `SPM015` | Error | `ReplaceEntityTypes` cannot replace a type with itself or with a `ReadOnly = true` target |
| `SPM016` | Error | Concrete entity type referenced in `[EntityRegistry]` `Configure(...)` does not belong to that registry |

## Manual Registration & Registry Conventions (Optional)

You can also register entities manually or apply registry-scoped rules directly inside `Configure(IEntityRegistryBuilder registry)`:

```csharp
public void Configure(IEntityRegistryBuilder registry)
{
    registry.HasDefaultSchema("sales");
    registry.Map<Order, OrderConfiguration>();
    registry.Map<Customer>();

    registry.ConfigureProperties<decimal>(c => c.HasPrecision(18, 6));
    registry.ConfigureEntities(ModelCreationPhase.BeforeEntityConfigClass, m =>
        m.Entity<Customer>(e => e.ToTable("customers")));
}
```

## Table & Key Methods

```csharp
// Table name, chained schema, comment, and rename hint
builder.ToTable("orders")
    .HasSchema("sales")
    .HasComment("Customer purchase orders")
    .HasPreviousTableName("customer_orders", previousSchema: "dbo");

// Set only the schema while keeping the convention table name
builder.HasSchema("sales");

// Exclude a read-only ref stub or database view from migration SQL generation
builder.ToTable("products").HasSchema("inventory").ExcludeFromMigrations();

// Attach custom runtime metadata annotations (forwarded by ApplySchemaToEfCore)
builder.SetAnnotation("Audit:TrackChanges", true);

// Ignore non-database properties (single or multiple via anonymous type)
builder.Ignore(x => x.TransientDisplayTotal);
builder.Ignore(x => new { x.CachedLabel, x.UiSelected });

// Default and named query filters (applied when using ApplySchemaToEfCore)
builder.HasQueryFilter(x => !x.IsDeleted);
builder.HasQueryFilter("SoftDelete", x => !x.IsDeleted);
builder.HasQueryFilter("Tenant", x => x.TenantId == currentTenantId);

// Single-column primary key with explicit constraint name
builder.HasKey(x => x.Id)
    .HasName("pk_orders");

// Composite primary key
builder.HasKey(x => new { x.OrderId, x.LineNumber })
    .HasName("pk_order_lines");
```

| Method | Purpose |
| --- | --- |
| `ToTable(name, schema?)` | Sets physical table name (and optional schema) |
| `HasSchema(schema)` | Sets schema (chained after `ToTable(...)` or standalone) |
| `HasPreviousTableName(oldName, oldSchema?)` | Hints table rename during `generate` |
| `HasComment(comment)` | Emits `COMMENT ON TABLE` |
| `ExcludeFromMigrations(bool = true)` | Keeps entity for queries/FKs and `ApplySchemaToEfCore`, but skips migration SQL |
| `SetAnnotation<TValue>(name, value)` / `GetAnnotation<TValue>(name)` / `HasAnnotation(name)` | Attaches or reads custom entity/column metadata (forwarded by `ApplySchemaToEfCore`) |
| `Ignore(x => x.Prop)` | Excludes property from schema & SQL |
| `HasQueryFilter(x => ...)` / `HasQueryFilter(key, x => ...)` | Query filters for `ApplySchemaToEfCore` |
| `HasKey(x => x.Id)` / `HasKey(...).HasName("pk_...")` | Single or composite PK and explicit constraint name |
| `OwnsOne(x => x.Nav, owned => ...)` | Inline table-split owned value objects (supports nested `OwnsOne`, `Property`, `Ignore`, `HasIndex`) |

## Owned Value Objects (`OwnsOne`)

Map value objects inline onto the owner's table using `OwnsOne` (columns default to `{Nav}_{Prop}` formatted with your naming convention):

```csharp
builder.OwnsOne(x => x.BillingAddress, a =>
{
    a.Property(x => x.City).HasMaxLength(100);
    a.Property(x => x.ZipCode).HasMaxLength(20);
    a.HasIndex(x => x.ZipCode);
    a.Ignore(x => x.DisplayLine);
});

// Nullable navigation (Address? ShippingAddress): all owned columns default to NULL
builder.OwnsOne(x => x.ShippingAddress, a =>
{
    a.Property(x => x.City).HasColumnName("ship_city").HasMaxLength(100);
    a.OwnsOne(x => x.Coordinates, g =>
    {
        g.Property(x => x.Latitude).HasPrecision(9, 6);
        g.Property(x => x.Longitude).HasPrecision(9, 6);
    });
});
```

When the navigation property is nullable (`Address?`), all mapped owned columns automatically default to `NULL` in SQL (even if the value object's C# properties are non-nullable). Explicitly calling `.IsRequired()` inside a nullable owned navigation is rejected by both `SchemaFinalizer` and `db-migrator doctor`.

## Column Configuration (`Property(x => x.Prop)`)

```csharp
builder.Property(x => x.Code)
    .HasColumnName("order_code")
    .HasMaxLength(32)
    .IsRequired()
    .HasDefaultValue("ORD-NEW");

builder.Property(x => x.FullTotal)
    .HasComputedColumnSql(expr => expr.Column(x => x.Subtotal).Math().Plus(x => x.Tax), stored: true);

builder.Property(x => x.RowVersion)
    .IsRowVersion(); // Uses PostgreSQL xmin when property is uint
```

| Category | Methods |
| --- | --- |
| **Naming & Types** | `HasColumnName`, `HasPreviousColumnName`, `HasColumnType`, `HasMaxLength`, `HasPrecision(p)` / `HasPrecision(p, s)`, `IsUnicode(bool)`, `IsRequired(bool)` |
| **Defaults & Generation** | `HasDefaultValue<TValue>(value)`, `HasDefaultValueSql(sql)`, `HasComputedColumnSql(expr / sql, stored: false)`, `ValueGeneratedOnAdd(isIdentity: true)`, `ValueGeneratedOnAddOrUpdate()`, `ValueGeneratedNever()` |
| **Concurrency & Metadata** | `IsConcurrencyToken()`, `IsRowVersion()`, `HasComment(text)`, `SetAnnotation<TValue>(name, value)` |
| **Type Conversions** | `HasConversion<TProvider>()` / `HasConversion(Type)`, plus `HasConversion(ValueConverter)` in `SharpPersistence.Migrator.EfCore` |
