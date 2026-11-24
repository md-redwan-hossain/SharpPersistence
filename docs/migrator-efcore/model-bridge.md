# Runtime Model Bridge

`SharpPersistence.Migrator.EfCore` lets your EF Core `DbContext` reuse the exact `DatabaseSchema` built by `AddDbMigrator(...)`, so you never maintain duplicate entity configurations for queries vs. migrations.

## Applying `DatabaseSchema` to `DbContext`

`AddDbMigrator(...)` registers `DatabaseSchema` as a singleton in DI. Inject it into your `DbContext` and call `modelBuilder.ApplySchemaToEfCore(schema)`:

```csharp
using Microsoft.EntityFrameworkCore;
using SharpPersistence.Migrator.EfCore;
using SharpPersistence.Migrator.Models;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    DatabaseSchema schema) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplySchemaToEfCore(schema);
    }
}
```

### What `ApplySchemaToEfCore` Transfers

* Table names, schemas, comments, ignored properties, and sequences
* Global and named query filters (`HasQueryFilter(...)`)
* Primary keys, column names, types, nullability, max length, precision/scale, defaults, identity, computed columns, concurrency tokens (`IsRowVersion`), and `ValueConverter`s
* Foreign keys (with `OnDelete` behavior), indexes (unique, filtered, included columns), and check constraints

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
