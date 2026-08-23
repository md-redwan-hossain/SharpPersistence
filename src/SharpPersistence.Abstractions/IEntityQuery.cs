using System.Linq.Expressions;

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

#if NET10_0_OR_GREATER
    IEntityQuery<T> IgnoreQueryFilters(IReadOnlyCollection<string> filterKeys);
#endif

    IEntityQuery<T> OffsetPaginate(int page, int limit);

    IEntityQuery<TResult> Select<TResult>(Expression<Func<T, TResult>> selector);

    Task<T?> GetOneAsync(CancellationToken cancellationToken = default);

    Task<IList<T>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<long> GetCountAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Expression<Func<T, bool>> condition, CancellationToken cancellationToken = default);

    Task<bool> EveryAsync(Expression<Func<T, bool>> condition, CancellationToken cancellationToken = default);
}