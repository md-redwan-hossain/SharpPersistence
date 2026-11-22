using System.Linq.Expressions;
using System.Numerics;

namespace SharpPersistence.Abstractions;

/// <summary>
/// Composable read query.
/// </summary>
public interface IEntityQuery<T>
{
    IEntityQuery<T> Where(Expression<Func<T, bool>> condition);

    IEntityQuery<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector);

    IEntityQuery<T> OrderByDesc<TKey>(Expression<Func<T, TKey>> keySelector);

    IEntityQuery<T> EnableTracking();

    IEntityQuery<T> IgnoreQueryFilters();

    IEntityQuery<T> IgnoreQueryFilters(IReadOnlyCollection<string> filterKeys);

    IEntityQuery<T> OffsetPaginate(int page, int limit);

    IEntityQuery<TResult> Select<TResult>(Expression<Func<T, TResult>> selector);

    Task<T?> GetFirstOrDefaultAsync(CancellationToken cancellationToken = default);

    Task<IList<T>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<long> GetCountAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Expression<Func<T, bool>> condition, CancellationToken cancellationToken = default);

    Task<bool> EveryAsync(Expression<Func<T, bool>> condition, CancellationToken cancellationToken = default);

    Task<TNumber> SumAsync<TNumber>(
        Expression<Func<T, TNumber>> selector,
        CancellationToken cancellationToken = default)
        where TNumber : INumber<TNumber>;

    Task<TResult?> MinAsync<TResult>(
        Expression<Func<T, TResult>> selector,
        CancellationToken cancellationToken = default);

    Task<TResult?> MaxAsync<TResult>(
        Expression<Func<T, TResult>> selector,
        CancellationToken cancellationToken = default);

    Task<TResult?> MinOrNullAsync<TResult>(
        Expression<Func<T, TResult>> selector,
        CancellationToken cancellationToken = default)
        where TResult : struct;

    Task<TResult?> MinOrNullAsync<TResult>(
        Expression<Func<T, TResult?>> selector,
        CancellationToken cancellationToken = default)
        where TResult : struct;

    Task<TResult?> MaxOrNullAsync<TResult>(
        Expression<Func<T, TResult>> selector,
        CancellationToken cancellationToken = default)
        where TResult : struct;

    Task<TResult?> MaxOrNullAsync<TResult>(
        Expression<Func<T, TResult?>> selector,
        CancellationToken cancellationToken = default)
        where TResult : struct;
}