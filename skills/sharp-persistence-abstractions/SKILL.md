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
- **Tracking & Filters:** `.EnableTracking()`, `.IgnoreQueryFilters(filterKeys)` *(.NET 10+)*
- **Pagination & Projection:** `.OffsetPaginate(page, limit)`, `.Select(selector)` *(returns `IEntityQuery<TResult>`)*
- **Terminals:**
  - `.GetOneAsync(ct)` -> `Task<T?>`
  - `.GetAllAsync(ct)` -> `Task<IList<T>>`
  - `.GetCountAsync(ct)` -> `Task<long>`
  - `.ExistsAsync(ct)` / `.ExistsAsync(condition, ct)` -> `Task<bool>`
  - `.EveryAsync(condition, ct)` -> `Task<bool>`

## Writes & Tracking

- **Create:** `Create(entity)`, `CreateMany(entities)`, `CreateAsync(entity)`, `CreateManyAsync(entities)`
- **Update:** `Update(entity)`, `UpdateMany(entities)`
- **Delete:** `Remove(entity)`, `RemoveMany(entities)`, `RemoveManyDirectAsync(condition)` *(executes bulk delete directly without loading entities)*
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
    .GetOneAsync(ct);

// Existence / universal check / bulk delete
bool hasPending = await orderRepo.Query()
    .Where(x => x.CustomerId == customerId)
    .ExistsAsync(x => x.Status == OrderStatus.Pending, ct);

int deleted = await orderRepo.RemoveManyDirectAsync(x => x.Status == OrderStatus.Draft);
```

## Replacing `IRepositoryBase<TEntity>` Overloads

| `IRepositoryBase<TEntity>` | `IRepositoryCore<TEntity>.Query()` |
| :--- | :--- |
| `GetOneSortedAsync(cond, (sort, desc), enableTracking: true, ct)` | `Query().Where(cond).OrderByDesc(sort).EnableTracking().GetOneAsync(ct)` |
| `GetAllSortedAndPaginatedSubsetAsync(page, limit, cond, sel, (sort, false), ct)` | `Query().Where(cond).OrderBy(sort).OffsetPaginate(page, limit).Select(sel).GetAllAsync(ct)` |
| `ExistsAsync(filter, match, ct)` | `Query().Where(filter).ExistsAsync(match, ct)` |
| `EveryAsync(filter, match, ct)` | `Query().Where(filter).EveryAsync(match, ct)` |
