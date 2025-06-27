namespace SharpPersistence.Extensions;

public static class QueryableExtensions
{
    public static IQueryable<T> QueryableOffsetPaginate<T>(this IQueryable<T> queryable, int page, int limit)
    {
        if (page <= 0)
        {
            page = 1;
        }

        if (limit <= 0)
        {
            limit = 1;
        }

        return queryable.Skip((page - 1) * limit).Take(limit);
    }
}