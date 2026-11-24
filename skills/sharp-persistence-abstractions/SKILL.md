---
name: sharp-persistence-abstractions
description: >-
  Use SharpPersistence.Abstractions IRepositoryCore<TEntity> and IEntityQuery<T> for composable,
  type-safe repository queries, projections, pagination, and write/tracking operations in C#.
---

# SharpPersistence.Abstractions (`IRepositoryCore<TEntity>`)

Use `IRepositoryCore<TEntity>` (`using SharpPersistence.Abstractions;`) for clean, composable repository queries and persistence operations instead of `IRepositoryBase<TEntity>`'s combinatorial overloads.

## Composable Reads (`Query()`)

`repo.Query()` returns `IEntityQuery<TEntity>` (non-tracking by default unless `.EnableTracking()` is called):

- **Filter & Sort:** `.Where(predicate)`, `.OrderBy(selector)`, `.OrderByDesc(selector)`
- **Tracking & Filters:** `.EnableTracking()`, `.IgnoreQueryFilters()`, `.IgnoreQueryFilters(filterKeys)`
- **Pagination & Projection:** `.OffsetPaginate(page, limit)`, `.Select(selector)` *(returns `IEntityQuery<TResult>`)*
- **Terminals:**
  - `.GetFirstOrDefaultAsync(ct)` -> `Task<T?>` (or `.Select(x => x.ValueProp).GetFirstOrNullAsync(ct)` for nullable struct return)
  - `.GetAllAsync(ct)` -> `Task<IList<T>>`
  - `.GetCountAsync(ct)` -> `Task<long>`
  - `.ExistsAsync(ct)` / `.ExistsAsync(condition, ct)` -> `Task<bool>`
  - `.EveryAsync(condition, ct)` -> `Task<bool>`
  - `.SumAsync(selector, ct)` *(for `int`, `long`, `decimal`, `double`, `float`)*
  - `.MinAsync(selector, ct)` / `.MaxAsync(selector, ct)` *(plus `.MinOrNullAsync` / `.MaxOrNullAsync` for nullable struct results on empty queries)*

## Writes & Tracking

- **Create:** `Create(entity)`, `CreateMany(entities)`, `CreateAsync(entity, ct)`, `CreateManyAsync(entities, ct)`
- **Update:** `Update(entity)`, `UpdateMany(entities)`
- **Delete:** `Remove(entity)`, `RemoveMany(entities)`, `RemoveManyDirectAsync(condition, ct)`, `RemoveManyDirectIgnoringFiltersAsync(condition, filterKeys, ct)` *(executes bulk delete directly without loading entities)*
- **Attach/Track:** `TrackEntity(entity)`, `TrackEntities(entities)`

## Examples

```csharp
// Filtered, sorted, paginated projection (no tracking)
var activeOrders = await orderRepo.Query()
    .Where(x => x.CustomerId == customerId && x.IsActive)
    .OrderByDesc(x => x.CreatedAt)
    .OffsetPaginate(page: 1, limit: 20)
    .Select(x => new OrderSummary(x.Id, x.Total))
    .GetAllAsync(ct);

// Single entity with change tracking for update
var order = await orderRepo.Query()
    .Where(x => x.Id == orderId)
    .EnableTracking()
    .GetFirstOrDefaultAsync(ct);

// Existence / universal check / bulk delete
bool hasPending = await orderRepo.Query()
    .Where(x => x.CustomerId == customerId)
    .ExistsAsync(x => x.Status == OrderStatus.Pending, ct);

int deleted = await orderRepo.RemoveManyDirectAsync(x => x.Status == OrderStatus.Draft, ct);

int purged = await orderRepo.RemoveManyDirectIgnoringFiltersAsync(
    x => x.IsDeleted,
    [AppDbContext.SoftDeleteFilter],
    ct);

// Aggregates (single column or computed expression)
decimal total = await orderRepo.Query()
    .Where(x => x.IsPaid)
    .SumAsync(x => x.Total, ct);

decimal netTotal = await orderRepo.Query()
    .Where(x => x.IsPaid)
    .SumAsync(x => x.Subtotal - x.Discount, ct);

DateTime? latestOrderAt = await orderRepo.Query()
    .Where(x => x.CustomerId == customerId)
    .MaxOrNullAsync(x => x.CreatedAt, ct);
```

## Migrating from `IRepositoryBase<TEntity>` / `RepositoryBase<TEntity, TDbContext>`

1. **Interface & Base Class:**
   - Replace `IRepositoryBase<TEntity>` with `IRepositoryCore<TEntity>`.
   - Replace `RepositoryBase<TEntity, TDbContext>` with `RepositoryCore<TEntity, TDbContext>`.
2. **Reads & Checks:**

| Legacy (`IRepositoryBase<TEntity>`) | New (`IRepositoryCore<TEntity>.Query()`) |
| :--- | :--- |
| `GetOneAsync(cond, ct)` | `Query().Where(cond).GetFirstOrDefaultAsync(ct)` |
| `GetOneAsync(cond, enableTracking: true, ct)` | `Query().Where(cond).EnableTracking().GetFirstOrDefaultAsync(ct)` |
| `GetOneSortedAsync(cond, (sort, true), ct)` | `Query().Where(cond).OrderByDesc(sort).GetFirstOrDefaultAsync(ct)` |
| `GetOneSubsetAsync(cond, sel, ct)` | `Query().Where(cond).Select(sel).GetFirstOrDefaultAsync(ct)` |
| `GetAllAsync(cond, ct)` | `Query().Where(cond).GetAllAsync(ct)` |
| `GetAllSortedAsync(cond, (sort, false), ct)` | `Query().Where(cond).OrderBy(sort).GetAllAsync(ct)` |
| `GetAllSortedAndPaginatedAsync(page, limit, cond, (sort, true), ct)` | `Query().Where(cond).OrderByDesc(sort).OffsetPaginate(page, limit).GetAllAsync(ct)` |
| `GetAllSortedAndPaginatedSubsetAsync(page, limit, cond, sel, (sort, false), ct)` | `Query().Where(cond).OrderBy(sort).OffsetPaginate(page, limit).Select(sel).GetAllAsync(ct)` |
| `GetCountAsync(cond, ct)` | `Query().Where(cond).GetCountAsync(ct)` |
| `ExistsAsync(filter, match, ct)` | `Query().Where(filter).ExistsAsync(match, ct)` |
| `EveryAsync(filter, match, ct)` | `Query().Where(filter).EveryAsync(match, ct)` |

## Compile-Time Entity Discovery Attributes

`SharpPersistence.Abstractions` provides zero-dependency domain attributes (and transitively includes the Roslyn entity manifest generator):

- `[Entity]` — built-in marker for single-registry projects.
- `EntityMarkerAttribute` + `[assembly: DiscoverEntitiesBy<TMarkerAttribute>]` — custom per-module marker attributes for modular monoliths.
- `[assembly: DisableEntityDiscovery]` (or MSBuild `<DisableEntityDiscovery>true</DisableEntityDiscovery>`) — opts an assembly out of source generation.

```csharp
using SharpPersistence.Abstractions;

// Custom module marker in Billing.Domain
public sealed class BillingEntityAttribute : EntityMarkerAttribute;

[assembly: DiscoverEntitiesBy<BillingEntityAttribute>]

[BillingEntity]
public sealed class Invoice
{
    public Guid Id { get; set; }
}
```
