using System.Data;
using System.Data.Common;

namespace SharpPersistence.Abstractions;

public interface IUnitOfWork
{
    void Save();
    Task SaveAsync(CancellationToken cancellationToken = default);
    DbTransaction? CurrentTransaction { get; }
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default);
    IUnitOfWorkTransaction BeginTransaction();
    IUnitOfWorkTransaction BeginTransaction(IsolationLevel isolationLevel);
    Task UseTransactionAsync(IUnitOfWorkTransaction transaction, CancellationToken cancellationToken = default);
    Task UseTransactionAsync(DbTransaction transaction, CancellationToken cancellationToken = default);
}
