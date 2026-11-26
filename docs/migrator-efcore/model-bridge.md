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

Instead of manually declaring `DbSet<T>` properties and filtering entities per `DbContext`, decorate a `sealed partial` `DbContext` with `[BindEntityRegistry<TRegistry>]`. The Roslyn generator emits `DbSet<T>` (or `IQueryable<T>` with `.AsNoTracking()` when `ReadOnly = true`) properties, a `(DbContextOptions<TContext>, DatabaseModel)` constructor (unless you declare your own), and `OnModelCreating` wired to `modelBuilder.ApplySchemaToEfCore(...)` plus a `partial void OnModelCreatingPartial(ModelBuilder modelBuilder)` hook:

```csharp
using Microsoft.EntityFrameworkCore;
using SharpPersistence.Migrator.EfCore.Attributes;

[BindEntityRegistry<SalesRegistry>]
[BindEntityRegistry<IdentityRegistry>(ReadOnly = true)] // emits IQueryable<User> Users => Set<User>().AsNoTracking();
[EntityRegistryItem<AuditLog>(Exclude = true)]          // omit specific entity from this DbContext
[EntityRegistryItem<Order>(Pluralize = false)]          // override property name (Order instead of Orders)
public sealed partial class SalesDbContext : DbContext
{
    // Optional hook that runs right after modelBuilder.ApplySchemaToEfCore(...)
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
    }
}
```

- **Registry-level options**: `[BindEntityRegistry<TRegistry>(ReadOnly = false, Pluralize = true)]`.
- **Per-entity overrides**: `[EntityRegistryItem<TEntity>(ReadOnly = ..., Pluralize = ..., Exclude = ...)]`.
- **Compile-time diagnostics & IDE CodeFixes (`SPM008`-`SPM013`)**: Validates that the `DbContext` is `sealed partial` and inherits `DbContext`, checks for duplicate bindings/items, flags unknown entities in `[EntityRegistryItem<TEntity>]`, catches property name collisions, and ensures `DatabaseModel` is wired into `ApplySchemaToEfCore`.

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
