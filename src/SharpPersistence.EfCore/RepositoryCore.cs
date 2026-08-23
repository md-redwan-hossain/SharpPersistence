using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SharpPersistence.Abstractions;
using SharpPersistence.EfCore.Internals;

namespace SharpPersistence.EfCore;

public abstract class RepositoryCore<TEntity, TDbContext> : IRepositoryCore<TEntity>
    where TEntity : class
    where TDbContext : DbContext
{
    protected readonly TDbContext DatabaseContext;
    protected readonly DbSet<TEntity> EntityDbSet;

    protected RepositoryCore(TDbContext context)
    {
        DatabaseContext = context;
        EntityDbSet = DatabaseContext.Set<TEntity>();
    }

    public IEntityQuery<TEntity> Query()
    {
        return new EntityQuery<TEntity>(EntityDbSet);
    }

    public async Task CreateAsync(TEntity entity)
    {
        await EntityDbSet.AddAsync(entity).ConfigureAwait(false);
    }

    public async Task CreateManyAsync(ICollection<TEntity> entity)
    {
        await EntityDbSet.AddRangeAsync(entity).ConfigureAwait(false);
    }

    public void Create(TEntity entity)
    {
        EntityDbSet.Add(entity);
    }

    public void CreateMany(ICollection<TEntity> entity)
    {
        EntityDbSet.AddRange(entity);
    }

    public virtual void Update(TEntity entityToUpdate)
    {
        EntityDbSet.Update(entityToUpdate);
    }

    public virtual void UpdateMany(ICollection<TEntity> entitiesToUpdate)
    {
        EntityDbSet.UpdateRange(entitiesToUpdate);
    }

    public virtual void Remove(TEntity entityToDelete)
    {
        EntityDbSet.Remove(entityToDelete);
    }

    public virtual void RemoveMany(ICollection<TEntity> entitiesToUpdate)
    {
        EntityDbSet.RemoveRange(entitiesToUpdate);
    }

    public virtual Task<int> RemoveManyDirectAsync(Expression<Func<TEntity, bool>> condition)
    {
        return EntityDbSet
            .Where(condition)
            .ExecuteDeleteAsync();
    }

    public void TrackEntity(TEntity entity)
    {
        DatabaseContext.Set<TEntity>().Attach(entity);
    }

    public void TrackEntities(IEnumerable<TEntity> entities)
    {
        DatabaseContext.Set<TEntity>().AttachRange(entities);
    }
}