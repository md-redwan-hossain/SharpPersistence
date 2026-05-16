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

    public virtual void Dispose()
    {
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }

    public virtual async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    public virtual void Save() => _dbContext.SaveChanges();

    public virtual async Task SaveAsync() => await _dbContext.SaveChangesAsync().ConfigureAwait(false);


    public DbTransaction? CurrentTransaction => _dbContext.Database.CurrentTransaction?.GetDbTransaction();

    public async Task<DbTransaction> BeginTransactionAsync()
    {
        var trx = await _dbContext.Database.BeginTransactionAsync().ConfigureAwait(false);
        return trx.GetDbTransaction();
    }

    public async Task<DbTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        var trx = await _dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        return trx.GetDbTransaction();
    }

    public async Task<DbTransaction> BeginTransactionAsync(IsolationLevel isolationLevel)
    {
        var trx = await _dbContext.Database
            .BeginTransactionAsync(isolationLevel)
            .ConfigureAwait(false);
        
        return trx.GetDbTransaction();
    }

    public async Task<DbTransaction> BeginTransactionAsync(IsolationLevel isolationLevel,
        CancellationToken cancellationToken)
    {
        var trx = await _dbContext.Database
            .BeginTransactionAsync(isolationLevel, cancellationToken)
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

    public void UseTransaction(DbTransaction transaction)
    {
        _dbContext.Database.UseTransaction(transaction);
    }

    public async Task UseTransactionAsync(DbTransaction transaction)
    {
        await _dbContext.Database.UseTransactionAsync(transaction).ConfigureAwait(false);
    }

    public async Task UseTransactionAsync(DbTransaction transaction, CancellationToken cancellationToken)
    {
        await _dbContext.Database.UseTransactionAsync(transaction, cancellationToken).ConfigureAwait(false);
    }
}