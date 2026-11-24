# Quick Start: Repositories & Unit of Work

Get up and running with `IRepositoryCore<TEntity>` and `IUnitOfWork` in 5 minutes.

## Why Use This?

Instead of calling `DbContext` directly in your application handlers (which tightly couples your business logic to EF Core) or writing dozens of `GetAllSortedAndPaginated...` repository methods, `IRepositoryCore<TEntity>` gives you a single composable `.Query()` pipeline that stays **non-tracking by default** and keeps your Domain/Application projects free of EF Core.

## 1. Install Packages

* **Domain / Application:** `dotnet add package SharpPersistence.Abstractions`
* **Infrastructure:** `dotnet add package SharpPersistence.EfCore`

## 2. Create a Repository

```csharp
// Application layer (only SharpPersistence.Abstractions)
public interface IOrderRepository : IRepositoryCore<Order>;

// Infrastructure layer (SharpPersistence.EfCore)
public sealed class OrderRepository(AppDbContext dbContext)
    : RepositoryCore<Order, AppDbContext>(dbContext), IOrderRepository;
```

## 3. Register in DI

```csharp
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork<AppDbContext>>();
```

## 4. Query, Update & Save

```csharp
// Read: non-tracking, filtered, sorted, paginated projection
var recentOrders = await orderRepo.Query()
    .Where(x => x.CustomerId == customerId && x.IsActive)
    .OrderByDesc(x => x.CreatedAt)
    .OffsetPaginate(page: 1, limit: 20)
    .Select(x => new OrderSummary(x.Id, x.Total))
    .GetAllAsync(ct);

// Update: opt in to tracking when you plan to modify the entity
var order = await orderRepo.Query()
    .Where(x => x.Id == orderId)
    .EnableTracking()
    .GetFirstOrDefaultAsync(ct);

if (order is not null)
{
    order.MarkPaid();
    await unitOfWork.SaveAsync(ct);
}
```

Next: [EF Core Repositories & Unit of Work Reference](/core/efcore-repositories)
