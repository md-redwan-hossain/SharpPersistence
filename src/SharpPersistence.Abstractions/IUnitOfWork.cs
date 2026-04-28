using System.Data;
using System.Data.Common;

namespace SharpPersistence.Abstractions;

public interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    void Save();
    Task SaveAsync();
    public DbTransaction? CurrentTransaction { get; }
    Task<DbTransaction> BeginTransactionAsync();
    Task<DbTransaction> BeginTransactionAsync(IsolationLevel isolationLevel);
    DbTransaction BeginTransaction();
    DbTransaction BeginTransaction(IsolationLevel isolationLevel);
}