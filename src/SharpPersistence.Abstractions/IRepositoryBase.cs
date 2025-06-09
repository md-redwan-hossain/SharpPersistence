using System.Linq.Expressions;

namespace SharpPersistence.Abstractions;

public interface IRepositoryBase<TEntity>
    where TEntity : class
{
    Task<int> UpdateDirectAsync<TProperty>(Expression<Func<TEntity, bool>> condition,
        Func<TEntity, TProperty> propertyExpression, TProperty valueExpression);
    Task CreateAsync(TEntity entity);
    Task CreateManyAsync(ICollection<TEntity> entity);

    Task<TEntity?> GetOneAsync(Expression<Func<TEntity, bool>> condition,
        CancellationToken cancellationToken = default);

    Task<TEntity?> GetOneAsync(Expression<Func<TEntity, bool>> condition,
        bool enableTracking, CancellationToken cancellationToken = default);

    Task<TEntity?> GetOneSortedAsync<TSorter>(Expression<Func<TEntity, bool>> condition,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        bool enableTracking, CancellationToken cancellationToken = default);

    Task<TEntity?> GetOneSortedAsync<TSorter>(Expression<Func<TEntity, bool>> condition,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        CancellationToken cancellationToken = default);

    Task<TResult?> GetOneSubsetAsync<TResult>(
        Expression<Func<TEntity, bool>> condition,
        Expression<Func<TEntity, TResult>> subsetSelector,
        bool enableTracking,
        CancellationToken cancellationToken = default);

    Task<TResult?> GetOneSubsetAsync<TResult>(
        Expression<Func<TEntity, bool>> condition,
        Expression<Func<TEntity, TResult>> subsetSelector,
        CancellationToken cancellationToken = default);

    Task<TResult?> GetOneSubsetAsync<TResult>(
        Expression<Func<TEntity, TResult>> subsetSelector,
        CancellationToken cancellationToken = default);

    Task<TResult?> GetOneSortedSubsetAsync<TSorter, TResult>(
        Expression<Func<TEntity, bool>> condition,
        Expression<Func<TEntity, TResult>> subsetSelector,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        bool enableTracking,
        CancellationToken cancellationToken = default);

    Task<TResult?> GetOneSortedSubsetAsync<TSorter, TResult>(
        Expression<Func<TEntity, bool>> condition,
        Expression<Func<TEntity, TResult>> subsetSelector,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        CancellationToken cancellationToken = default);

    Task<ICollection<TResult>> GetAllSortedAndPaginatedSubsetAsync<TResult, TSorter>(
        int page, int limit,
        Expression<Func<TEntity, TResult>> subsetSelector,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        CancellationToken cancellationToken = default)
        where TSorter : IComparable<TSorter>;

    Task<ICollection<TResult>> GetAllSortedAndPaginatedSubsetAsync<TResult, TSorter>(
        int page, int limit,
        Expression<Func<TEntity, bool>> condition,
        Expression<Func<TEntity, TResult>> subsetSelector,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        CancellationToken cancellationToken = default)
        where TSorter : IComparable<TSorter>;

    Task<ICollection<TResult>> GetAllSortedAndPaginatedSubsetAsync<TResult, TSorter>(
        int page, int limit,
        Expression<Func<TEntity, TResult>> subsetSelector,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        bool enableTracking,
        CancellationToken cancellationToken = default)
        where TSorter : IComparable<TSorter>;

    Task<ICollection<TResult>> GetAllSortedAndPaginatedSubsetAsync<TResult, TSorter>(
        int page, int limit,
        Expression<Func<TEntity, bool>> condition,
        Expression<Func<TEntity, TResult>> subsetSelector,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        bool enableTracking,
        CancellationToken cancellationToken = default)
        where TSorter : IComparable<TSorter>;


    Task<ICollection<TEntity>> GetAllSortedAndPaginatedAsync<TSorter>(
        int page, int limit,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        CancellationToken cancellationToken = default)
        where TSorter : IComparable<TSorter>;

    Task<ICollection<TEntity>> GetAllSortedAndPaginatedAsync<TSorter>(
        int page, int limit,
        Expression<Func<TEntity, bool>> condition,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        CancellationToken cancellationToken = default)
        where TSorter : IComparable<TSorter>;

    Task<ICollection<TEntity>> GetAllSortedAndPaginatedAsync<TSorter>(
        int page, int limit,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        bool enableTracking,
        CancellationToken cancellationToken = default)
        where TSorter : IComparable<TSorter>;

    Task<ICollection<TEntity>> GetAllSortedAndPaginatedAsync<TSorter>(
        int page, int limit,
        Expression<Func<TEntity, bool>> condition,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        bool enableTracking,
        CancellationToken cancellationToken = default)
        where TSorter : IComparable<TSorter>;


    Task<ICollection<TResult>> GetAllSortedSubsetAsync<TResult, TSorter>(
        Expression<Func<TEntity, TResult>> subsetSelector,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        CancellationToken cancellationToken = default)
        where TSorter : IComparable<TSorter>;

    Task<ICollection<TResult>> GetAllSortedSubsetAsync<TResult, TSorter>(
        Expression<Func<TEntity, bool>> condition,
        Expression<Func<TEntity, TResult>> subsetSelector,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        CancellationToken cancellationToken = default)
        where TSorter : IComparable<TSorter>;

    Task<ICollection<TResult>> GetAllSortedSubsetAsync<TResult, TSorter>(
        Expression<Func<TEntity, TResult>> subsetSelector,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        bool enableTracking,
        CancellationToken cancellationToken = default)
        where TSorter : IComparable<TSorter>;

    Task<ICollection<TResult>> GetAllSortedSubsetAsync<TResult, TSorter>(
        Expression<Func<TEntity, bool>> condition,
        Expression<Func<TEntity, TResult>> subsetSelector,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        bool enableTracking,
        CancellationToken cancellationToken = default)
        where TSorter : IComparable<TSorter>;

    Task<ICollection<TEntity>> GetAllSortedAsync<TSorter>(
        Expression<Func<TEntity, bool>> condition,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        bool enableTracking,
        CancellationToken cancellationToken = default)
        where TSorter : IComparable<TSorter>;

    Task<ICollection<TEntity>> GetAllSortedAsync<TSorter>(
        Expression<Func<TEntity, bool>> condition,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        CancellationToken cancellationToken = default)
        where TSorter : IComparable<TSorter>;

    Task<ICollection<TEntity>> GetAllSortedAsync<TSorter>(
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        bool enableTracking,
        CancellationToken cancellationToken = default)
        where TSorter : IComparable<TSorter>;

    Task<ICollection<TEntity>> GetAllSortedAsync<TSorter>(
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        CancellationToken cancellationToken = default)
        where TSorter : IComparable<TSorter>;

    Task<ICollection<TResult>> GetAllSubsetAsync<TResult>(
        Expression<Func<TEntity, bool>> condition,
        Expression<Func<TEntity, TResult>> subsetSelector,
        bool enableTracking,
        CancellationToken cancellationToken = default);

    Task<ICollection<TResult>> GetAllSubsetAsync<TResult>(
        Expression<Func<TEntity, bool>> condition,
        Expression<Func<TEntity, TResult>> subsetSelector,
        CancellationToken cancellationToken = default);

    Task<ICollection<TResult>> GetAllSubsetAsync<TResult>(
        Expression<Func<TEntity, TResult>> subsetSelector,
        bool enableTracking,
        CancellationToken cancellationToken = default);

    Task<ICollection<TResult>> GetAllSubsetAsync<TResult>(
        Expression<Func<TEntity, TResult>> subsetSelector,
        CancellationToken cancellationToken = default);

    Task<ICollection<TEntity>> GetAllAsync(
        Expression<Func<TEntity, bool>> condition,
        bool enableTracking,
        CancellationToken cancellationToken = default);

    Task<ICollection<TEntity>> GetAllAsync(
        Expression<Func<TEntity, bool>> condition,
        CancellationToken cancellationToken = default);

    Task<ICollection<TEntity>> GetAllAsync(
        bool enableTracking,
        CancellationToken cancellationToken = default);

    Task<ICollection<TEntity>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> condition,
        CancellationToken cancellationToken = default);

    Task<bool> EveryAsync(Expression<Func<TEntity, bool>> condition,
        CancellationToken cancellationToken = default);

    Task<long> GetCountAsync(Expression<Func<TEntity, bool>> condition,
        CancellationToken cancellationToken = default);

    Task<long> GetCountAsync(CancellationToken cancellationToken = default);

    void Update(TEntity entityToUpdate);

    void UpdateMany(ICollection<TEntity> entitiesToUpdate);
    
    void Remove(TEntity entityToDelete);

    void RemoveMany(ICollection<TEntity> entitiesToUpdate);

    Task<int> RemoveManyDirectAsync(Expression<Func<TEntity, bool>> condition);

    void TrackEntity(TEntity entity);
    void TrackEntities(IEnumerable<TEntity> entities);
}