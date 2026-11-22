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

    public async Task CreateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await EntityDbSet.AddAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    public async Task CreateManyAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        await EntityDbSet.AddRangeAsync(entities, cancellationToken).ConfigureAwait(false);
    }

    public void Create(TEntity entity)
    {
        EntityDbSet.Add(entity);
    }

    public void CreateMany(IEnumerable<TEntity> entities)
    {
        EntityDbSet.AddRange(entities);
    }

    public virtual void Update(TEntity entityToUpdate)
    {
        EntityDbSet.Update(entityToUpdate);
    }

    public virtual void UpdateMany(IEnumerable<TEntity> entitiesToUpdate)
    {
        EntityDbSet.UpdateRange(entitiesToUpdate);
    }

    public virtual void Remove(TEntity entityToDelete)
    {
        EntityDbSet.Remove(entityToDelete);
    }

    public virtual void RemoveMany(IEnumerable<TEntity> entitiesToDelete)
    {
        EntityDbSet.RemoveRange(entitiesToDelete);
    }

    public virtual Task<int> RemoveManyDirectAsync(
        Expression<Func<TEntity, bool>> condition,
        CancellationToken cancellationToken = default)
    {
        return EntityDbSet
            .Where(condition)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public virtual Task<int> RemoveManyDirectIgnoringFiltersAsync(
        Expression<Func<TEntity, bool>> condition,
        IReadOnlyCollection<string> filterKeys,
        CancellationToken cancellationToken = default)
    {
        return EntityDbSet
            .IgnoreQueryFilters(filterKeys)
            .Where(condition)
            .ExecuteDeleteAsync(cancellationToken);
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