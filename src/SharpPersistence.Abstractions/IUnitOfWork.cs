using System.Data;
using System.Data.Common;

namespace SharpPersistence.Abstractions;

public interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    void Save();
    Task SaveAsync();
    DbConnection GetDbConnection();
    Task<DbTransaction> BeginTransactionAsync();
    Task<DbTransaction> BeginTransactionAsync(IsolationLevel isolationLevel);
    DbTransaction BeginTransaction();
    DbTransaction BeginTransaction(IsolationLevel isolationLevel);
}