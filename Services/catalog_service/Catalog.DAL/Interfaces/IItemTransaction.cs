namespace Catalog.DAL.Interfaces;

public interface IItemTransaction
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken);
}
