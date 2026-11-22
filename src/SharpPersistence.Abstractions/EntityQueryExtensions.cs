namespace SharpPersistence.Abstractions;

/// <summary>
/// Convenience extensions for <see cref="IEntityQuery{T}"/>.
/// </summary>
public static class EntityQueryExtensions
{
    public static Task<T?> GetFirstOrNullAsync<T>(
        this IEntityQuery<T> query,
        CancellationToken cancellationToken = default)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Select(x => (T?)x).GetFirstOrDefaultAsync(cancellationToken);
    }
}
