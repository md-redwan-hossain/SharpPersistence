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

    IEntityQuery<T> AsTracking();

    IEntityQuery<T> AsNoTracking();

#if NET10_0_OR_GREATER
    IEntityQuery<T> IgnoreQueryFilters(IReadOnlyCollection<string> filterKeys);
#endif

    IEntityQuery<T> OffsetPaginate(int page, int limit);

    IEntityQuery<TResult> Select<TResult>(Expression<Func<T, TResult>> selector);

    Task<T?> FirstOrDefaultAsync(CancellationToken cancellationToken = default);

    Task<IList<T>> ToListAsync(CancellationToken cancellationToken = default);

    Task<long> CountAsync(CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    Task<bool> AllAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
}