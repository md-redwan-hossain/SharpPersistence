using System.Linq.Expressions;
using System.Numerics;
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

    public abstract IEntityQuery<T> EnableTracking();

    public abstract IEntityQuery<T> IgnoreQueryFilters();

    public abstract IEntityQuery<T> IgnoreQueryFilters(IReadOnlyCollection<string> filterKeys);

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

    public Task<T?> GetFirstOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        return Query.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IList<T>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await Query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<long> GetCountAsync(CancellationToken cancellationToken = default)
    {
        return Query.LongCountAsync(cancellationToken);
    }

    public Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
    {
        return Query.AnyAsync(cancellationToken);
    }

    public Task<bool> ExistsAsync(Expression<Func<T, bool>> condition, CancellationToken cancellationToken = default)
    {
        return Query.AnyAsync(condition, cancellationToken);
    }

    public Task<bool> EveryAsync(Expression<Func<T, bool>> condition, CancellationToken cancellationToken = default)
    {
        return Query.AllAsync(condition, cancellationToken);
    }

    public async Task<TNumber> SumAsync<TNumber>(
        Expression<Func<T, TNumber>> selector,
        CancellationToken cancellationToken = default)
        where TNumber : INumber<TNumber>
    {
        object result = typeof(TNumber) switch
        {
            var t when t == typeof(decimal) => await Query.SumAsync((Expression<Func<T, decimal>>)(object)selector, cancellationToken).ConfigureAwait(false),
            var t when t == typeof(int) => await Query.SumAsync((Expression<Func<T, int>>)(object)selector, cancellationToken).ConfigureAwait(false),
            var t when t == typeof(long) => await Query.SumAsync((Expression<Func<T, long>>)(object)selector, cancellationToken).ConfigureAwait(false),
            var t when t == typeof(double) => await Query.SumAsync((Expression<Func<T, double>>)(object)selector, cancellationToken).ConfigureAwait(false),
            var t when t == typeof(float) => await Query.SumAsync((Expression<Func<T, float>>)(object)selector, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException($"SumAsync is not supported for {typeof(TNumber).Name}.")
        };

        return (TNumber)result;
    }

    public Task<TResult?> MinAsync<TResult>(
        Expression<Func<T, TResult>> selector,
        CancellationToken cancellationToken = default)
    {
        return Query.MinAsync(selector, cancellationToken)!;
    }

    public Task<TResult?> MaxAsync<TResult>(
        Expression<Func<T, TResult>> selector,
        CancellationToken cancellationToken = default)
    {
        return Query.MaxAsync(selector, cancellationToken)!;
    }

    public Task<TResult?> MinOrNullAsync<TResult>(
        Expression<Func<T, TResult>> selector,
        CancellationToken cancellationToken = default)
        where TResult : struct
    {
        var expr = Expression.Lambda<Func<T, TResult?>>(
            Expression.Convert(selector.Body, typeof(TResult?)),
            selector.Parameters);

        return Query.MinAsync(expr, cancellationToken);
    }

    public Task<TResult?> MinOrNullAsync<TResult>(
        Expression<Func<T, TResult?>> selector,
        CancellationToken cancellationToken = default)
        where TResult : struct
    {
        return Query.MinAsync(selector, cancellationToken);
    }

    public Task<TResult?> MaxOrNullAsync<TResult>(
        Expression<Func<T, TResult>> selector,
        CancellationToken cancellationToken = default)
        where TResult : struct
    {
        var expr = Expression.Lambda<Func<T, TResult?>>(
            Expression.Convert(selector.Body, typeof(TResult?)),
            selector.Parameters);

        return Query.MaxAsync(expr, cancellationToken);
    }

    public Task<TResult?> MaxOrNullAsync<TResult>(
        Expression<Func<T, TResult?>> selector,
        CancellationToken cancellationToken = default)
        where TResult : struct
    {
        return Query.MaxAsync(selector, cancellationToken);
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