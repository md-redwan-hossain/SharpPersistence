using System.Data;
using System.Data.Common;

namespace SharpPersistence.Abstractions;

public interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    void Save();
    Task SaveAsync();
    public DbTransaction? CurrentTransaction { get; }
    Task<DbTransaction> BeginTransactionAsync();
    Task<DbTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
    Task<DbTransaction> BeginTransactionAsync(IsolationLevel isolationLevel);
    Task<DbTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken);
    DbTransaction BeginTransaction();
    DbTransaction BeginTransaction(IsolationLevel isolationLevel);
    Task UseTransactionAsync(DbTransaction transaction);
    Task UseTransactionAsync(DbTransaction transaction, CancellationToken cancellationToken);
}