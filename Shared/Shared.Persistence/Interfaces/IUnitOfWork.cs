namespace Shared.Persistence.Interfaces;

public interface IUnitOfWork : IDisposable
{
    Task<int> SaveAsync(CancellationToken cancellationToken = default);
    Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default);
    Task<bool> SaveChangeAsync();
}