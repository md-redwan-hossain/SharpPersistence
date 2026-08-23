using System.Linq.Expressions;

namespace SharpPersistence.Abstractions;

public interface IRepositoryCore<TEntity>
    where TEntity : class
{
    IEntityQuery<TEntity> Query();
    void Create(TEntity entity);
    void CreateMany(ICollection<TEntity> entity);
    Task CreateAsync(TEntity entity);
    Task CreateManyAsync(ICollection<TEntity> entity);
    void Update(TEntity entityToUpdate);

    void UpdateMany(ICollection<TEntity> entitiesToUpdate);

    void Remove(TEntity entityToDelete);

    void RemoveMany(ICollection<TEntity> entitiesToUpdate);

    Task<int> RemoveManyDirectAsync(Expression<Func<TEntity, bool>> condition);

    void TrackEntity(TEntity entity);
    void TrackEntities(IEnumerable<TEntity> entities);
}