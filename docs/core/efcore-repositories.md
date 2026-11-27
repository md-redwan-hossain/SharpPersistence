# EF Core Repositories & Unit of Work

`SharpPersistence.EfCore` implements `IRepositoryCore<TEntity>` and `IUnitOfWork` on top of `DbContext`.

## Registering Repositories & Unit of Work

```csharp
public interface IOrderRepository : IRepositoryCore<Order>;

public sealed class OrderRepository(AppDbContext dbContext)
    : RepositoryCore<Order, AppDbContext>(dbContext), IOrderRepository;
```

Register in DI:

```csharp
services.AddScoped<IOrderRepository, OrderRepository>();
services.AddScoped<IUnitOfWork, UnitOfWork<AppDbContext>>();
```

## Composable Queries (`Query()`)

`repo.Query()` returns `IEntityQuery<TEntity>` with **non-tracking enabled by default** (`AsNoTracking`).

```csharp
// Filtered, sorted, paginated projection (no change tracking)
var summaries = await orderRepo.Query()
    .Where(x => x.CustomerId == customerId && x.IsActive)
    .OrderByDesc(x => x.CreatedAt)
    .OffsetPaginate(page: 1, limit: 20)
    .Select(x => new OrderSummary(x.Id, x.Total))
    .GetAllAsync(ct);

// Single entity with change tracking enabled for updates
var order = await orderRepo.Query()
    .Where(x => x.Id == orderId)
    .EnableTracking()
    .GetFirstOrDefaultAsync(ct);

// Nullable struct projection terminal
DateTime? shippedAt = await orderRepo.Query()
    .Where(x => x.Id == orderId)
    .Select(x => x.ShippedAt)
    .GetFirstOrNullAsync(ct);
```

### Query Filters

Bypass all EF Core global query filters or only specific named filters:

```csharp
// Ignore all filters
var allOrders = await orderRepo.Query()
    .IgnoreQueryFilters()
    .GetAllAsync(ct);

// Ignore specific named filters
var softDeleted = await orderRepo.Query()
    .IgnoreQueryFilters([AppDbContext.SoftDeleteFilter])
    .Where(x => x.IsDeleted)
    .GetAllAsync(ct);
```

### Aggregates & Checks

```csharp
long count = await orderRepo.Query()
    .Where(x => x.IsPaid)
    .GetCountAsync(ct);

bool hasPending = await orderRepo.Query()
    .Where(x => x.CustomerId == customerId)
    .ExistsAsync(x => x.Status == OrderStatus.Pending, ct);

bool allShipped = await orderRepo.Query()
    .Where(x => x.BatchId == batchId)
    .EveryAsync(x => x.Status == OrderStatus.Shipped, ct);

decimal total = await orderRepo.Query()
    .Where(x => x.IsPaid)
    .SumAsync(x => x.Subtotal - x.Discount, ct);

DateTime? latest = await orderRepo.Query()
    .Where(x => x.CustomerId == customerId)
    .MaxOrNullAsync(x => x.CreatedAt, ct);
```

## Writes, Bulk Deletes & Transactions

```csharp
// Add / Update / Remove
await orderRepo.CreateAsync(newOrder, ct);
orderRepo.Update(existingOrder);
orderRepo.Remove(obsoleteOrder);

// Direct SQL bulk delete without loading entities
int purged = await orderRepo.RemoveManyDirectAsync(
    x => x.Status == OrderStatus.Draft && x.CreatedAt < cutoff,
    ct);

// Direct bulk delete ignoring specific query filters
int hardDeleted = await orderRepo.RemoveManyDirectIgnoringFiltersAsync(
    x => x.IsDeleted,
    [AppDbContext.SoftDeleteFilter],
    ct);

// Unit of Work transaction (returns IUnitOfWorkTransaction; use tx.UnderlyingTransaction for Dapper/ADO.NET)
await using var tx = await unitOfWork.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
await unitOfWork.SaveAsync(ct);
await tx.CommitAsync(ct);
```

## Migrating from `IRepositoryBase<TEntity>`

| Legacy (`IRepositoryBase<TEntity>`) | Modern (`IRepositoryCore<TEntity>.Query()`) |
| --- | --- |
| `GetOneAsync(cond, ct)` | `Query().Where(cond).GetFirstOrDefaultAsync(ct)` |
| `GetOneAsync(cond, enableTracking: true, ct)` | `Query().Where(cond).EnableTracking().GetFirstOrDefaultAsync(ct)` |
| `GetOneSortedAsync(cond, (sort, true), ct)` | `Query().Where(cond).OrderByDesc(sort).GetFirstOrDefaultAsync(ct)` |
| `GetOneSubsetAsync(cond, sel, ct)` | `Query().Where(cond).Select(sel).GetFirstOrDefaultAsync(ct)` |
| `GetAllAsync(cond, ct)` | `Query().Where(cond).GetAllAsync(ct)` |
| `GetAllSortedAndPaginatedAsync(p, l, cond, (sort, true), ct)` | `Query().Where(cond).OrderByDesc(sort).OffsetPaginate(p, l).GetAllAsync(ct)` |
| `GetCountAsync(cond, ct)` | `Query().Where(cond).GetCountAsync(ct)` |
| `ExistsAsync(filter, match, ct)` | `Query().Where(filter).ExistsAsync(match, ct)` |
| `EveryAsync(filter, match, ct)` | `Query().Where(filter).EveryAsync(match, ct)` |
