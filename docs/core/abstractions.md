# Abstractions & Discovery Attributes

`SharpPersistence.Abstractions` is a **zero-dependency** package designed for your Domain and Application layers. It provides repository/unit-of-work contracts and compile-time entity discovery attributes without pulling EF Core or database drivers into your domain.

## Repository & Unit of Work Contracts

| Interface | Role |
| --- | --- |
| `IRepositoryCore<TEntity>` | Composable, non-tracking-by-default repository (`Query()`, writes, bulk deletes) |
| `IEntityQuery<T>` | Fluent query pipeline (`.Where`, `.OrderBy`, `.OffsetPaginate`, `.Select`, `.EnableTracking`, `.IgnoreQueryFilters`, async terminals) |
| `IUnitOfWork` | `Save()`, `SaveAsync(ct)`, `BeginTransaction(...)`, `BeginTransactionAsync(...)` (returning `IUnitOfWorkTransaction`), `UseTransactionAsync(...)`, `CurrentTransaction` |
| `IRepositoryBase<TEntity>` | Legacy overload-based repository interface (prefer `IRepositoryCore<TEntity>` for new code) |

See [EF Core Repositories & Unit of Work](/core/efcore-repositories) for full usage examples.

## Entity Discovery Attributes

`SharpPersistence.Abstractions` includes the compile-time entity discovery generator automatically for every project that references it (`using SharpPersistence.Abstractions.Attributes;`):

### Built-in `[Entity]` Marker

For single-registry apps, mark domain classes with `[Entity]` (pass `ReadOnly = true` to automatically exclude read-only reference entities or views from migrations):

```csharp
using SharpPersistence.Abstractions.Attributes;

namespace MyApp.Domain;

[Entity]
public sealed class Invoice
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
}
```

### Per-Module Entity Groups (`[Entity<TGroup>]`)

In a modular monolith, declare an empty `readonly record struct` implementing `IEntityGroup` and pair `[Entity<TGroup>]` 1-to-1 with `[EntityRegistry<TGroup>]`:

```csharp
using SharpPersistence.Abstractions.Attributes;

// Billing.Domain/BillingGroup.cs
public readonly record struct BillingGroup : IEntityGroup;

// Billing.Domain/Payment.cs
[Entity<BillingGroup>]
public sealed class Payment
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
}

// Read-only cross-module reference stub (excluded from migrations)
[Entity<BillingGroup>(ReadOnly = true)]
public sealed class RefUser
{
    public Guid Id { get; set; }
}
```

### Disabling Discovery in an Assembly

If an assembly (such as a test or shared DTO project) should opt out of entity discovery, add this to its `AssemblyInfo.cs`:

```csharp
// AssemblyInfo.cs
using SharpPersistence.Abstractions.Attributes;

[assembly: DisableEntityDiscovery]
```

You can also disable it via MSBuild: `<DisableEntityDiscovery>true</DisableEntityDiscovery>`.

## SQL Parser & Value Objects

`SharpPersistence.Abstractions` also includes `IParsedSqlStorage` (see [SQL Parser & ParsedSqlStorage](/core/sql-parser)), SQL value objects (`ParsedSql`, `SqlFile`, `SqlLine`, `SqlBlockInfo`), and query pagination helpers (`PagedData<T>`, `EntityKey`, `KeyData`).
