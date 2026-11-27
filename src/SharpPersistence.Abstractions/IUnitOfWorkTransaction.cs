using System.Data.Common;

namespace SharpPersistence.Abstractions;

public interface IUnitOfWorkTransaction : IDisposable, IAsyncDisposable
{
    DbTransaction UnderlyingTransaction { get; }

    void Commit();
    void Rollback();
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
