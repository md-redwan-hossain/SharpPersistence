using System.Data.Common;

namespace SharpPersistence.Abstractions;

public interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    void Save();
    Task SaveAsync();
    Task<DbTransaction> BeginTransactionAsync();
    DbTransaction BeginTransaction();
}