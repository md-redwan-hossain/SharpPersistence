# Abstractions & Discovery Attributes

`SharpPersistence.Abstractions` is a **zero-dependency** package designed for your Domain and Application layers. It provides repository/unit-of-work contracts and compile-time entity discovery attributes without pulling EF Core or database drivers into your domain.

## Repository & Unit of Work Contracts

| Interface | Role |
| --- | --- |
| `IRepositoryCore<TEntity>` | Composable, non-tracking-by-default repository (`Query()`, writes, bulk deletes) |
| `IEntityQuery<T>` | Fluent query pipeline (`.Where`, `.OrderBy`, `.OffsetPaginate`, `.Select`, `.EnableTracking`, `.IgnoreQueryFilters`, async terminals) |
| `IUnitOfWork` | `Save()`, `SaveAsync(ct)`, `BeginTransaction(...)`, `BeginTransactionAsync(...)`, `UseTransactionAsync(...)`, `CurrentTransaction` |
| `IRepositoryBase<TEntity>` | Legacy overload-based repository interface (prefer `IRepositoryCore<TEntity>` for new code) |

See [EF Core Repositories & UoW](/core/efcore-repositories) for full usage examples.

## Entity Discovery Attributes

`SharpPersistence.Abstractions` includes the compile-time entity discovery generator automatically for every project that references it, while keeping your domain attributes free of migration-specific names:

### Built-in `[Entity]` Marker

For single-registry apps, mark domain classes with `[Entity]`:

```csharp
using SharpPersistence.Abstractions;

namespace MyApp.Domain;

[Entity]
public sealed class Invoice
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
}
```

### Custom Per-Module Marker Attributes

In a modular monolith, each module can define its own marker attribute inheriting from `EntityMarkerAttribute` and opt in via `[assembly: DiscoverEntitiesBy<TMarkerAttribute>]`:

```csharp
using SharpPersistence.Abstractions;

// 1. Define a module marker
public sealed class BillingEntityAttribute : EntityMarkerAttribute;

// 2. Register it at assembly level in Billing.Domain
[assembly: DiscoverEntitiesBy<BillingEntityAttribute>]

// 3. Mark domain entities
[BillingEntity]
public sealed class Payment
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
}
```

### Disabling Discovery in an Assembly

If an assembly (such as a test or shared DTO project) should opt out of source generation:

```csharp
using SharpPersistence.Abstractions;

[assembly: DisableEntityDiscovery]
```

You can also disable it via MSBuild: `<DisableEntityDiscovery>true</DisableEntityDiscovery>`.

## SQL Parser & Value Objects

`SharpPersistence.Abstractions` also includes value objects for SQL script parsing (`ParsedSql`, `SqlFile`, `SqlLine`, `SqlBlockInfo`, `IParsedSqlStorage`) and query pagination (`PagedData<T>`, `EntityKey`, `KeyData`).
