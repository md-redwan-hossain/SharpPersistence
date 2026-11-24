namespace WebApiExample.Contracts;

public sealed record CategoryDto(
    long Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt);

public sealed record CreateCategoryRequest(
    string Name,
    string? Description);
