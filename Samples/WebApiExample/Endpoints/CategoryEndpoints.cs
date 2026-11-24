using Microsoft.EntityFrameworkCore;
using WebApiExample.Contracts;
using WebApiExample.Entities;
using WebApiExample.Persistence;

namespace WebApiExample.Endpoints;

public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/categories");

        group.MapGet("/", GetAllAsync);
        group.MapPost("/", CreateAsync);
        group.MapDelete("/{id:long}", DeleteAsync);

        return app;
    }

    private static async Task<IResult> GetAllAsync(AppDbContext db, CancellationToken ct)
    {
        var items = await db.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Description, c.CreatedAt))
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    private static async Task<IResult> CreateAsync(CreateCategoryRequest request, AppDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { error = "Category name is required." });
        }

        var trimmedName = request.Name.Trim();
        if (await db.Categories.AnyAsync(c => c.Name == trimmedName, ct))
        {
            return Results.Conflict(new { error = $"Category '{trimmedName}' already exists." });
        }

        var category = new Category
        {
            Name = trimmedName,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim()
        };

        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);

        var dto = new CategoryDto(category.Id, category.Name, category.Description, category.CreatedAt);
        return Results.Created($"/api/categories/{category.Id}", dto);
    }

    private static async Task<IResult> DeleteAsync(long id, AppDbContext db, CancellationToken ct)
    {
        var category = await db.Categories.FindAsync([id], ct);
        if (category is null)
        {
            return Results.NotFound();
        }

        if (await db.Products.AnyAsync(p => p.CategoryId == id, ct))
        {
            return Results.Conflict(new { error = "Cannot delete a category that still has products." });
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
