using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SharpPersistence.Abstractions;

namespace SharpPersistence.EfCore.Internals;

/// <summary>
/// Shared base class for <see cref="EntityQuery{T}"/> and <see cref="EntityProjectedQuery{T}"/>
/// </summary>
internal abstract class EntityQueryBase<T> : IEntityQuery<T>
{
    protected IQueryable<T> Query;
    protected bool Ordered;

    protected EntityQueryBase(IQueryable<T> query)
    {
        Query = query;
    }

    public IEntityQuery<T> Where(Expression<Func<T, bool>> condition)
    {
        Query = Query.Where(condition);
        return this;
    }

    public IEntityQuery<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        if (Ordered && Query is IOrderedQueryable<T> ordered)
        {
            Query = ordered.ThenBy(keySelector);
        }
        else
        {
            Query = Query.OrderBy(keySelector);
            Ordered = true;
        }

        return this;
    }

    public IEntityQuery<T> OrderByDesc<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        if (Ordered && Query is IOrderedQueryable<T> ordered)
        {
            Query = ordered.ThenByDescending(keySelector);
        }
        else
        {
            Query = Query.OrderByDescending(keySelector);
            Ordered = true;
        }

        return this;
    }

    public abstract IEntityQuery<T> AsTracking();

    public abstract IEntityQuery<T> AsNoTracking();

#if NET10_0_OR_GREATER
    public abstract IEntityQuery<T> IgnoreQueryFilters(IReadOnlyCollection<string> filterKeys);
#endif

    public IEntityQuery<T> OffsetPaginate(int page, int limit)
    {
        (page, limit) = NormalizePagination(page, limit);
        Query = Query.Skip((page - 1) * limit).Take(limit);
        return this;
    }

    public IEntityQuery<TResult> Select<TResult>(Expression<Func<T, TResult>> selector)
    {
        return new EntityProjectedQuery<TResult>(Query.Select(selector));
    }

    public Task<T?> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        return Query.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IList<T>> ToListAsync(CancellationToken cancellationToken = default)
    {
        return await Query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        return Query.LongCountAsync(cancellationToken);
    }

    public Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        return Query.AnyAsync(cancellationToken);
    }

    public Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return Query.AnyAsync(predicate, cancellationToken);
    }

    public Task<bool> AllAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return Query.AllAsync(predicate, cancellationToken);
    }

    private static (int page, int limit) NormalizePagination(int page, int limit)
    {
        if (page <= 0)
        {
            page = 1;
        }

        if (limit <= 0)
        {
            limit = 1;
        }

        return (page, limit);
    }
}
