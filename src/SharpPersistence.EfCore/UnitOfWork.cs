using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SharpPersistence.Abstractions;
using SharpPersistence.EfCore.Internals;

namespace SharpPersistence.EfCore;

public abstract class UnitOfWork<TDbContext> : IUnitOfWork
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;

    protected UnitOfWork(TDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public virtual void Save() => _dbContext.SaveChanges();

    public virtual async Task SaveAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

    public DbTransaction? CurrentTransaction =>
        _dbContext.Database.CurrentTransaction?.GetDbTransaction();

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var trx = await _dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        return new EfCoreUnitOfWorkTransaction(trx);
    }

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken = default)
    {
        var trx = await _dbContext.Database
            .BeginTransactionAsync(isolationLevel, cancellationToken)
            .ConfigureAwait(false);
        return new EfCoreUnitOfWorkTransaction(trx);
    }

    public IUnitOfWorkTransaction BeginTransaction() =>
        new EfCoreUnitOfWorkTransaction(_dbContext.Database.BeginTransaction());

    public IUnitOfWorkTransaction BeginTransaction(IsolationLevel isolationLevel) =>
        new EfCoreUnitOfWorkTransaction(_dbContext.Database.BeginTransaction(isolationLevel));

    public async Task UseTransactionAsync(
        IUnitOfWorkTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        await _dbContext.Database
            .UseTransactionAsync(transaction.UnderlyingTransaction, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UseTransactionAsync(
        DbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        await _dbContext.Database
            .UseTransactionAsync(transaction, cancellationToken)
            .ConfigureAwait(false);
    }
}
