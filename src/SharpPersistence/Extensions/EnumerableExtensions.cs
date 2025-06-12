namespace SharpPersistence.Extensions;

public static class EnumerableExtensions
{
    public static IEnumerable<T> OffsetPaginate<T>(this IEnumerable<T> enumerable, int page, int limit)
    {
        if (page <= 0)
        {
            page = 1;
        }

        if (limit <= 0)
        {
            limit = 1;
        }

        return enumerable.Skip((page - 1) * limit).Take(limit);
    }
}