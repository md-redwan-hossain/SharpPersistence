namespace WebApiExample.Contracts;

public sealed record ProductDto(
    long Id,
    long CategoryId,
    string CategoryName,
    string Name,
    string Sku,
    decimal Price,
    int StockQuantity,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed record CreateProductRequest(
    long CategoryId,
    string Name,
    string Sku,
    decimal Price,
    int StockQuantity,
    bool IsActive = true);

public sealed record UpdateProductRequest(
    long CategoryId,
    string Name,
    string Sku,
    decimal Price,
    int StockQuantity,
    bool IsActive);
