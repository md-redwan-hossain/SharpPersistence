using Microsoft.EntityFrameworkCore;
using WebApiExample.Entities;

namespace WebApiExample.Persistence;

public static class DatabaseSeeder
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.MigrateAsync(ct);

        if (await db.Categories.AnyAsync(ct))
        {
            return;
        }

        var electronics = new Category { Name = "Electronics", Description = "Devices and accessories" };
        var books = new Category { Name = "Books", Description = "Technical and fiction books" };
        db.Categories.AddRange(electronics, books);
        await db.SaveChangesAsync(ct);

        db.Products.AddRange(
            new Product
            {
                CategoryId = electronics.Id,
                Name = "Mechanical Keyboard",
                Sku = "ELEC-KB-001",
                Price = 129.99m,
                StockQuantity = 25
            },
            new Product
            {
                CategoryId = books.Id,
                Name = "PostgreSQL Internals",
                Sku = "BOOK-PG-001",
                Price = 49.50m,
                StockQuantity = 40
            });
        await db.SaveChangesAsync(ct);
    }
}
