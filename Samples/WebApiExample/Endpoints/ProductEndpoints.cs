using Microsoft.EntityFrameworkCore;
using WebApiExample.Contracts;
using WebApiExample.Entities;
using WebApiExample.Persistence;

namespace WebApiExample.Endpoints;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products");

        group.MapGet("/", GetAllAsync);
        group.MapGet("/{id:long}", GetByIdAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{id:long}", UpdateAsync);
        group.MapDelete("/{id:long}", DeleteAsync);

        return app;
    }

    private static async Task<IResult> GetAllAsync(long? categoryId, string? search, AppDbContext db, CancellationToken ct)
    {
        var query = db.Products.AsNoTracking().AsQueryable();

        if (categoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == categoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(p => EF.Functions.ILike(p.Name, pattern) || EF.Functions.ILike(p.Sku, pattern));
        }

        var items = await query
            .OrderBy(p => p.Id)
            .Select(p => new ProductDto(
                p.Id,
                p.CategoryId,
                p.Category.Name,
                p.Name,
                p.Sku,
                p.Price,
                p.StockQuantity,
                p.IsActive,
                p.CreatedAt))
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    private static async Task<IResult> GetByIdAsync(long id, AppDbContext db, CancellationToken ct)
    {
        var item = await db.Products
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new ProductDto(
                p.Id,
                p.CategoryId,
                p.Category.Name,
                p.Name,
                p.Sku,
                p.Price,
                p.StockQuantity,
                p.IsActive,
                p.CreatedAt))
            .FirstOrDefaultAsync(ct);

        return item is null ? Results.NotFound() : Results.Ok(item);
    }

    private static async Task<IResult> CreateAsync(CreateProductRequest request, AppDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Sku))
        {
            return Results.BadRequest(new { error = "Product name and SKU are required." });
        }

        if (request.Price < 0 || request.StockQuantity < 0)
        {
            return Results.BadRequest(new { error = "Price and stock quantity must be non-negative." });
        }

        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CategoryId, ct);
        if (category is null)
        {
            return Results.BadRequest(new { error = $"Category '{request.CategoryId}' does not exist." });
        }

        var trimmedSku = request.Sku.Trim();
        if (await db.Products.AnyAsync(p => p.Sku == trimmedSku, ct))
        {
            return Results.Conflict(new { error = $"Product with SKU '{trimmedSku}' already exists." });
        }

        var product = new Product
        {
            CategoryId = request.CategoryId,
            Name = request.Name.Trim(),
            Sku = trimmedSku,
            Price = request.Price,
            StockQuantity = request.StockQuantity,
            IsActive = request.IsActive
        };

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);

        var dto = new ProductDto(
            product.Id,
            product.CategoryId,
            category.Name,
            product.Name,
            product.Sku,
            product.Price,
            product.StockQuantity,
            product.IsActive,
            product.CreatedAt);

        return Results.Created($"/api/products/{product.Id}", dto);
    }

    private static async Task<IResult> UpdateAsync(long id, UpdateProductRequest request, AppDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Sku))
        {
            return Results.BadRequest(new { error = "Product name and SKU are required." });
        }

        if (request.Price < 0 || request.StockQuantity < 0)
        {
            return Results.BadRequest(new { error = "Price and stock quantity must be non-negative." });
        }

        var product = await db.Products.FindAsync([id], ct);
        if (product is null)
        {
            return Results.NotFound();
        }

        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CategoryId, ct);
        if (category is null)
        {
            return Results.BadRequest(new { error = $"Category '{request.CategoryId}' does not exist." });
        }

        var trimmedSku = request.Sku.Trim();
        if (await db.Products.AnyAsync(p => p.Sku == trimmedSku && p.Id != id, ct))
        {
            return Results.Conflict(new { error = $"Product with SKU '{trimmedSku}' already exists." });
        }

        product.CategoryId = request.CategoryId;
        product.Name = request.Name.Trim();
        product.Sku = trimmedSku;
        product.Price = request.Price;
        product.StockQuantity = request.StockQuantity;
        product.IsActive = request.IsActive;

        await db.SaveChangesAsync(ct);

        var dto = new ProductDto(
            product.Id,
            product.CategoryId,
            category.Name,
            product.Name,
            product.Sku,
            product.Price,
            product.StockQuantity,
            product.IsActive,
            product.CreatedAt);

        return Results.Ok(dto);
    }

    private static async Task<IResult> DeleteAsync(long id, AppDbContext db, CancellationToken ct)
    {
        var deleted = await db.Products
            .Where(p => p.Id == id)
            .ExecuteDeleteAsync(ct);

        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }
}
