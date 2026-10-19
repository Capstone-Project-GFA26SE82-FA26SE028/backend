namespace Catalog.DAL.Interfaces;

public interface IPriceTransaction
{
    Task<T> ExecuteAsync<T>(Guid itemId, Func<Task<T>> action, CancellationToken cancellationToken);
}
