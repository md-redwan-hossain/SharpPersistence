using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SharpPersistence.Abstractions;

namespace SharpPersistence.EfCore;

public abstract class UnitOfWork<TDbContext> : IUnitOfWork
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;

    protected UnitOfWork(TDbContext dbContext) => _dbContext = dbContext;

    public virtual void Dispose() => _dbContext.Dispose();

    public virtual ValueTask DisposeAsync() => _dbContext.DisposeAsync();

    public virtual void Save() => _dbContext.SaveChanges();

    public virtual async Task SaveAsync() => await _dbContext.SaveChangesAsync();

    public async Task<DbTransaction> BeginTransactionAsync()
    {
        var trx = await _dbContext.Database.BeginTransactionAsync().ConfigureAwait(false);
        return trx.GetDbTransaction();
    }

    public DbTransaction BeginTransaction()
    {
        var trx = _dbContext.Database.BeginTransaction();
        return trx.GetDbTransaction();
    }
}