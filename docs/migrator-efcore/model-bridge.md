# Runtime Model Bridge

`SharpPersistence.Migrator.EfCore` lets your EF Core `DbContext` reuse the exact `DatabaseModel` built by `AddDbMigrator(...)`, so you never maintain duplicate entity configurations for queries vs. migrations.

## Applying `DatabaseModel` to `DbContext`

`AddDbMigrator(...)` registers `DatabaseModel` as a singleton in DI. Inject it into your `DbContext` and call `modelBuilder.ApplySchemaToEfCore(model)`:

```csharp
using Microsoft.EntityFrameworkCore;
using SharpPersistence.Migrator.EfCore;
using SharpPersistence.Migrator.Models;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    DatabaseModel model) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplySchemaToEfCore(model);
    }
}
```

### What `ApplySchemaToEfCore` Transfers

* Table names, schemas, comments, ignored properties, and sequences
* Inline owned value objects (`OwnsOne`, including nested `OwnsOne` and owned indexes)
* Global and named query filters (`HasQueryFilter(...)`)
* Primary keys, column names, types, nullability, max length, precision/scale, defaults, identity, computed columns, concurrency tokens (`IsRowVersion`), and `ValueConverter`s
* Foreign keys (with `OnDelete` behavior), indexes (unique, filtered, included columns), and check constraints

## Compile-Time `DbContext` Registry Binding (`[BindEntityRegistry<TRegistry>]`)

Instead of manually declaring `DbSet<T>` properties and filtering entities per `DbContext`, decorate a `partial` `DbContext` with `[BindEntityRegistry<TRegistry>]`. The Roslyn generator emits `DbSet<T>` (or `IQueryable<T>` with `.AsNoTracking()` when `ReadOnly = true`) properties and a scoped `ApplyBoundRegistries(modelBuilder, model)` method:

```csharp
using Microsoft.EntityFrameworkCore;
using SharpPersistence.Migrator.EfCore.Attributes;
using SharpPersistence.Migrator.Models;

[BindEntityRegistry<SalesRegistry>]
[BindEntityRegistry<IdentityRegistry>(ReadOnly = true)] // emits IQueryable<User> Users => Set<User>().AsNoTracking();
[EntityRegistryItem<AuditLog>(Exclude = true)]          // omit specific entity from this DbContext
[EntityRegistryItem<Order>(Pluralize = false)]          // override property name (Order instead of Orders)
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

- **Registry-level options**: `[BindEntityRegistry<TRegistry>(ReadOnly = false, Pluralize = true)]`.
- **Per-entity overrides**: `[EntityRegistryItem<TEntity>(ReadOnly = ..., Pluralize = ..., Exclude = ...)]`.
- **Compile-time diagnostics (`SPM008`-`SPM013`)**: Validates that the `DbContext` is `partial` and inherits `DbContext`, checks for duplicate bindings/items, and flags unknown entities in `[EntityRegistryItem<TEntity>]`.

## Value Converters (`HasConversion`)

`HasConversion<TProvider>()` and `HasConversion(Type)` are built directly into `ColumnBuilder` (`SharpPersistence.Migrator`). When you also need an EF Core `ValueConverter` at runtime, `SharpPersistence.Migrator.EfCore` adds `HasConversion(ValueConverter)` so both SQL generation and `DbContext` stay in sync:

```csharp
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SharpPersistence.Migrator.EfCore;

// 1. Provider-type conversion (built into SharpPersistence.Migrator, e.g. enum -> string)
builder.Property(x => x.Status)
    .HasConversion<string>()
    .HasMaxLength(32);

// 2. Custom EF Core ValueConverter instance (via SharpPersistence.Migrator.EfCore)
builder.Property(x => x.Money)
    .HasConversion(new ValueConverter<Money, decimal>(
        m => m.Amount,
        d => new Money(d)))
    .HasPrecision(18, 2);
```
