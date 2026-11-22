using System.Linq.Expressions;

namespace SharpPersistence.Abstractions;

public interface IRepositoryCore<TEntity>
    where TEntity : class
{
    IEntityQuery<TEntity> Query();
    void Create(TEntity entity);
    void CreateMany(IEnumerable<TEntity> entities);
    Task CreateAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task CreateManyAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    void Update(TEntity entityToUpdate);

    void UpdateMany(IEnumerable<TEntity> entitiesToUpdate);

    void Remove(TEntity entityToDelete);

    void RemoveMany(IEnumerable<TEntity> entitiesToDelete);

    Task<int> RemoveManyDirectAsync(Expression<Func<TEntity, bool>> condition, CancellationToken cancellationToken = default);

    Task<int> RemoveManyDirectIgnoringFiltersAsync(
        Expression<Func<TEntity, bool>> condition,
        IReadOnlyCollection<string> filterKeys,
        CancellationToken cancellationToken = default);

    void TrackEntity(TEntity entity);
    void TrackEntities(IEnumerable<TEntity> entities);
}