using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;
using SharpPersistence.Abstractions;

namespace SharpPersistence.EfCore.Internals;

internal sealed class EfCoreUnitOfWorkTransaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction
{
    public DbTransaction UnderlyingTransaction => transaction.GetDbTransaction();

    public void Commit() => transaction.Commit();

    public void Rollback() => transaction.Rollback();

    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        transaction.CommitAsync(cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken = default) =>
        transaction.RollbackAsync(cancellationToken);

    public void Dispose() => transaction.Dispose();

    public ValueTask DisposeAsync() => transaction.DisposeAsync();
}
