using System.Data;
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
    public DbConnection GetDbConnection() => _dbContext.Database.GetDbConnection();

    public async Task<DbTransaction> BeginTransactionAsync()
    {
        var trx = await _dbContext.Database.BeginTransactionAsync().ConfigureAwait(false);
        return trx.GetDbTransaction();
    }

    public async Task<DbTransaction> BeginTransactionAsync(IsolationLevel isolationLevel)
    {
        var trx = await _dbContext.Database
            .BeginTransactionAsync(isolationLevel)
            .ConfigureAwait(false);

        return trx.GetDbTransaction();
    }

    public DbTransaction BeginTransaction()
    {
        var trx = _dbContext.Database.BeginTransaction();
        return trx.GetDbTransaction();
    }

    public DbTransaction BeginTransaction(IsolationLevel isolationLevel)
    {
        var trx = _dbContext.Database.BeginTransaction(isolationLevel);
        return trx.GetDbTransaction();
    }
}