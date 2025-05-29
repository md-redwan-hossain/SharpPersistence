using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SharpPersistence.Abstractions;

namespace SharpPersistence;

public abstract class RepositoryBase<TEntity, TDbContext> : IRepositoryBase<TEntity>
    where TEntity : class
    where TDbContext : DbContext
{
    protected readonly TDbContext DatabaseContext;
    protected readonly DbSet<TEntity> EntityDbSet;

    protected RepositoryBase(TDbContext context)
    {
        DatabaseContext = context;
        EntityDbSet = DatabaseContext.Set<TEntity>();
    }

    public async Task<TEntity?> GetOneAsync(Expression<Func<TEntity, bool>> condition,
        CancellationToken cancellationToken = default)
    {
        return await GetOneAsync(condition, enableTracking: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TEntity?> GetOneAsync(Expression<Func<TEntity, bool>> condition, bool enableTracking,
        CancellationToken cancellationToken = default)
    {
        var query = EntityDbSet.Where(condition);

        if (!enableTracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TEntity?> GetOneSortedAsync<TSorter>(Expression<Func<TEntity, bool>> condition,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter, bool enableTracking,
        CancellationToken cancellationToken = default)
    {
        var query = EntityDbSet.Where(condition);

        query = sorter.desc
            ? query.OrderByDescending(sorter.orderBy)
            : query.OrderBy(sorter.orderBy);

        if (!enableTracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TEntity?> GetOneSortedAsync<TSorter>(Expression<Func<TEntity, bool>> condition,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter, CancellationToken cancellationToken = default)
    {
        return await GetOneSortedAsync(condition, sorter, enableTracking: false, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<TResult?> GetOneSortedSubsetAsync<TSorter, TResult>(
        Expression<Func<TEntity, bool>> condition,
        Expression<Func<TEntity, TResult>> subsetSelector,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        bool enableTracking,
        CancellationToken cancellationToken = default)
    {
        var query = EntityDbSet.Where(condition);

        query = sorter.desc
            ? query.OrderByDescending(sorter.orderBy)
            : query.OrderBy(sorter.orderBy);

        if (!enableTracking)
        {
            query = query.AsNoTracking();
        }

        return await query
            .Select(subsetSelector)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<TResult?> GetOneSortedSubsetAsync<TSorter, TResult>(
        Expression<Func<TEntity, bool>> condition,
        Expression<Func<TEntity, TResult>> subsetSelector,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        CancellationToken cancellationToken = default)
    {
        return GetOneSortedSubsetAsync(condition, subsetSelector, sorter, enableTracking: false, cancellationToken);
    }

    public async Task<TResult?> GetOneSubsetAsync<TResult>(Expression<Func<TEntity, bool>> condition,
        Expression<Func<TEntity, TResult>> subsetSelector,
        bool enableTracking, CancellationToken cancellationToken = default)
    {
        var query = EntityDbSet.Where(condition);

        if (!enableTracking)
        {
            query = query.AsNoTracking();
        }

        return await query
            .Select(subsetSelector)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<TResult?> GetOneSubsetAsync<TResult>(Expression<Func<TEntity, bool>> condition,
        Expression<Func<TEntity, TResult>> subsetSelector,
        CancellationToken cancellationToken = default)
    {
        return GetOneSubsetAsync(condition, subsetSelector, enableTracking: false, cancellationToken);
    }

    public Task<TResult?> GetOneSubsetAsync<TResult>(Expression<Func<TEntity, TResult>> subsetSelector,
        CancellationToken cancellationToken = default)
    {
        return EntityDbSet
            .AsNoTracking()
            .Select(subsetSelector)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ICollection<TResult>> GetAllSortedAndPaginatedSubsetAsync<TResult, TSorter>(int page, int limit,
        Expression<Func<TEntity, TResult>> subsetSelector,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        CancellationToken cancellationToken = default)
        where TSorter : IComparable<TSorter>
    {
        var query = EntityDbSet.AsQueryable();

        query = sorter.desc
            ? query.OrderByDescending(sorter.orderBy)
            : query.OrderBy(sorter.orderBy);

        query = query.AsNoTracking();

        var safePageLimit = AvoidNegativeOrZeroPagination(page, limit);

        return await query
            .Skip((safePageLimit.page - 1) * safePageLimit.limit)
            .Take(safePageLimit.limit)
            .Select(subsetSelector)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<ICollection<TResult>> GetAllSortedAndPaginatedSubsetAsync<TResult, TSorter>(int page, int limit,
        Expression<Func<TEntity, bool>> condition,
        Expression<Func<TEntity, TResult>> subsetSelector,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        CancellationToken cancellationToken = default) where TSorter : IComparable<TSorter>
    {
        return GetAllSortedAndPaginatedSubsetAsync(page, limit, condition, subsetSelector, sorter,
            enableTracking: false, cancellationToken);
    }

    public async Task<ICollection<TResult>> GetAllSortedAndPaginatedSubsetAsync<TResult, TSorter>(int page, int limit,
        Expression<Func<TEntity, TResult>> subsetSelector,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter, bool enableTracking,
        CancellationToken cancellationToken = default) where TSorter : IComparable<TSorter>
    {
        var query = EntityDbSet.AsQueryable();

        query = sorter.desc
            ? query.OrderByDescending(sorter.orderBy)
            : query.OrderBy(sorter.orderBy);

        if (!enableTracking)
        {
            query = query.AsNoTracking();
        }

        var safePageLimit = AvoidNegativeOrZeroPagination(page, limit);

        return await query
            .Skip((safePageLimit.page - 1) * safePageLimit.limit)
            .Take(safePageLimit.limit)
            .Select(subsetSelector)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ICollection<TResult>> GetAllSortedAndPaginatedSubsetAsync<TResult, TSorter>(int page, int limit,
        Expression<Func<TEntity, bool>> condition, Expression<Func<TEntity, TResult>> subsetSelector,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        bool enableTracking, CancellationToken cancellationToken = default) where TSorter : IComparable<TSorter>
    {
        var query = EntityDbSet.Where(condition);

        query = sorter.desc
            ? query.OrderByDescending(sorter.orderBy)
            : query.OrderBy(sorter.orderBy);

        if (!enableTracking)
        {
            query = query.AsNoTracking();
        }

        var safePageLimit = AvoidNegativeOrZeroPagination(page, limit);

        return await query
            .Skip((safePageLimit.page - 1) * safePageLimit.limit)
            .Take(safePageLimit.limit)
            .Select(subsetSelector)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ICollection<TEntity>> GetAllSortedAndPaginatedAsync<TSorter>(int page, int limit,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter, CancellationToken cancellationToken = default)
        where TSorter : IComparable<TSorter>
    {
        var query = EntityDbSet.AsQueryable();

        query = sorter.desc
            ? query.OrderByDescending(sorter.orderBy)
            : query.OrderBy(sorter.orderBy);

        query = query.AsNoTracking();

        var safePageLimit = AvoidNegativeOrZeroPagination(page, limit);

        return await query
            .Skip((safePageLimit.page - 1) * safePageLimit.limit)
            .Take(safePageLimit.limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<ICollection<TEntity>> GetAllSortedAndPaginatedAsync<TSorter>(int page, int limit,
        Expression<Func<TEntity, bool>> condition, (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        CancellationToken cancellationToken = default) where TSorter : IComparable<TSorter>
    {
        return GetAllSortedAndPaginatedAsync(page, limit, condition, sorter, enableTracking: false,
            cancellationToken);
    }

    public async Task<ICollection<TEntity>> GetAllSortedAndPaginatedAsync<TSorter>(int page, int limit,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter, bool enableTracking,
        CancellationToken cancellationToken = default) where TSorter : IComparable<TSorter>
    {
        var query = EntityDbSet.AsQueryable();

        query = sorter.desc
            ? query.OrderByDescending(sorter.orderBy)
            : query.OrderBy(sorter.orderBy);

        if (!enableTracking)
        {
            query = query.AsNoTracking();
        }

        var safePageLimit = AvoidNegativeOrZeroPagination(page, limit);

        return await query
            .Skip((safePageLimit.page - 1) * safePageLimit.limit)
            .Take(safePageLimit.limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ICollection<TEntity>> GetAllSortedAndPaginatedAsync<TSorter>(int page, int limit,
        Expression<Func<TEntity, bool>> condition, (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        bool enableTracking, CancellationToken cancellationToken = default) where TSorter : IComparable<TSorter>
    {
        var query = EntityDbSet.Where(condition);

        query = sorter.desc
            ? query.OrderByDescending(sorter.orderBy)
            : query.OrderBy(sorter.orderBy);

        if (!enableTracking)
        {
            query = query.AsNoTracking();
        }

        var safePageLimit = AvoidNegativeOrZeroPagination(page, limit);

        return await query
            .Skip((safePageLimit.page - 1) * safePageLimit.limit)
            .Take(safePageLimit.limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ICollection<TResult>> GetAllSortedSubsetAsync<TResult, TSorter>(
        Expression<Func<TEntity, TResult>> subsetSelector,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter, CancellationToken cancellationToken = default)
        where TSorter : IComparable<TSorter>
    {
        var query = EntityDbSet.AsQueryable();

        query = sorter.desc
            ? query.OrderByDescending(sorter.orderBy)
            : query.OrderBy(sorter.orderBy);

        query = query.AsNoTracking();

        return await query
            .Select(subsetSelector)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<ICollection<TResult>> GetAllSortedSubsetAsync<TResult, TSorter>(
        Expression<Func<TEntity, bool>> condition,
        Expression<Func<TEntity, TResult>> subsetSelector,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        CancellationToken cancellationToken = default) where TSorter : IComparable<TSorter>
    {
        return GetAllSortedSubsetAsync(condition, subsetSelector, sorter, enableTracking: false, cancellationToken);
    }

    public async Task<ICollection<TResult>> GetAllSortedSubsetAsync<TResult, TSorter>(
        Expression<Func<TEntity, TResult>> subsetSelector,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter, bool enableTracking,
        CancellationToken cancellationToken = default) where TSorter : IComparable<TSorter>
    {
        var query = EntityDbSet.AsQueryable();

        query = sorter.desc
            ? query.OrderByDescending(sorter.orderBy)
            : query.OrderBy(sorter.orderBy);

        if (!enableTracking)
        {
            query = query.AsNoTracking();
        }

        return await query
            .Select(subsetSelector)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ICollection<TResult>> GetAllSortedSubsetAsync<TResult, TSorter>(
        Expression<Func<TEntity, bool>> condition,
        Expression<Func<TEntity, TResult>> subsetSelector,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        bool enableTracking, CancellationToken cancellationToken = default) where TSorter : IComparable<TSorter>
    {
        var query = EntityDbSet.Where(condition);

        query = sorter.desc
            ? query.OrderByDescending(sorter.orderBy)
            : query.OrderBy(sorter.orderBy);

        if (!enableTracking)
        {
            query = query.AsNoTracking();
        }

        return await query
            .Select(subsetSelector)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ICollection<TEntity>> GetAllSortedAsync<TSorter>(Expression<Func<TEntity, bool>> condition,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter, bool enableTracking,
        CancellationToken cancellationToken = default) where TSorter : IComparable<TSorter>
    {
        var query = EntityDbSet.Where(condition);

        query = sorter.desc
            ? query.OrderByDescending(sorter.orderBy)
            : query.OrderBy(sorter.orderBy);

        if (!enableTracking)
        {
            query = query.AsNoTracking();
        }

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<ICollection<TEntity>> GetAllSortedAsync<TSorter>(Expression<Func<TEntity, bool>> condition,
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter,
        CancellationToken cancellationToken = default) where TSorter : IComparable<TSorter>
    {
        return GetAllSortedAsync(condition, sorter, enableTracking: false, cancellationToken);
    }

    public async Task<ICollection<TEntity>> GetAllSortedAsync<TSorter>(
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter, bool enableTracking,
        CancellationToken cancellationToken = default) where TSorter : IComparable<TSorter>
    {
        var query = EntityDbSet.AsQueryable();

        query = sorter.desc
            ? query.OrderByDescending(sorter.orderBy)
            : query.OrderBy(sorter.orderBy);

        if (!enableTracking)
        {
            query = query.AsNoTracking();
        }

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ICollection<TEntity>> GetAllSortedAsync<TSorter>(
        (Expression<Func<TEntity, TSorter>> orderBy, bool desc) sorter, CancellationToken cancellationToken = default)
        where TSorter : IComparable<TSorter>
    {
        var query = EntityDbSet.AsQueryable();

        query = sorter.desc
            ? query.OrderByDescending(sorter.orderBy)
            : query.OrderBy(sorter.orderBy);

        query = query.AsNoTracking();

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ICollection<TResult>> GetAllSubsetAsync<TResult>(Expression<Func<TEntity, bool>> condition,
        Expression<Func<TEntity, TResult>> subsetSelector,
        bool enableTracking, CancellationToken cancellationToken = default)
    {
        var query = EntityDbSet.Where(condition);

        if (!enableTracking)
        {
            query = query.AsNoTracking();
        }

        return await query
            .Select(subsetSelector)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<ICollection<TResult>> GetAllSubsetAsync<TResult>(
        Expression<Func<TEntity, bool>> condition, Expression<Func<TEntity, TResult>> subsetSelector,
        CancellationToken cancellationToken = default)
    {
        return GetAllSubsetAsync(condition, subsetSelector, enableTracking: false, cancellationToken);
    }

    public async Task<ICollection<TResult>> GetAllSubsetAsync<TResult>(
        Expression<Func<TEntity, TResult>> subsetSelector, bool enableTracking,
        CancellationToken cancellationToken = default)
    {
        var query = EntityDbSet.AsQueryable();

        if (!enableTracking)
        {
            query = query.AsNoTracking();
        }

        return await query
            .Select(subsetSelector)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ICollection<TResult>> GetAllSubsetAsync<TResult>(
        Expression<Func<TEntity, TResult>> subsetSelector, CancellationToken cancellationToken = default)
    {
        var query = EntityDbSet.AsQueryable();

        query = query.AsNoTracking();

        return await query
            .Select(subsetSelector)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ICollection<TEntity>> GetAllAsync(Expression<Func<TEntity, bool>> condition, bool enableTracking,
        CancellationToken cancellationToken = default)
    {
        var query = EntityDbSet.Where(condition);

        if (!enableTracking)
        {
            query = query.AsNoTracking();
        }

        return await query
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ICollection<TEntity>> GetAllAsync(Expression<Func<TEntity, bool>> condition,
        CancellationToken cancellationToken = default)
    {
        return await GetAllAsync(condition, enableTracking: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ICollection<TEntity>> GetAllAsync(bool enableTracking,
        CancellationToken cancellationToken = default)
    {
        var query = EntityDbSet.AsQueryable();

        if (!enableTracking)
        {
            query = query.AsNoTracking();
        }

        return await query
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ICollection<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var query = EntityDbSet.AsQueryable();

        query = query.AsNoTracking();

        return await query
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public virtual Task<bool> EveryAsync(Expression<Func<TEntity, bool>> condition,
        CancellationToken cancellationToken = default)
    {
        return EntityDbSet.AllAsync(condition, cancellationToken);
    }

    public virtual Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> condition,
        CancellationToken cancellationToken = default)
    {
        return EntityDbSet.AnyAsync(condition, cancellationToken);
    }

    public virtual Task<long> GetCountAsync(Expression<Func<TEntity, bool>> condition,
        CancellationToken cancellationToken = default)
    {
        return EntityDbSet.LongCountAsync(condition, cancellationToken);
    }

    public virtual Task<long> GetCountAsync(CancellationToken cancellationToken = default)
    {
        return EntityDbSet.LongCountAsync(cancellationToken);
    }

    public async Task CreateAsync(TEntity entity)
    {
        await EntityDbSet.AddAsync(entity).ConfigureAwait(false);
    }

    public async Task CreateManyAsync(ICollection<TEntity> entity)
    {
        await EntityDbSet.AddRangeAsync(entity).ConfigureAwait(false);
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

    private static (int page, int limit) AvoidNegativeOrZeroPagination(int page, int limit)
    {
        var pagination = (page, limit);

        if (page <= 0)
        {
            pagination.page = 1;
        }

        if (limit <= 0)
        {
            pagination.limit = 1;
        }

        return pagination;
    }
}