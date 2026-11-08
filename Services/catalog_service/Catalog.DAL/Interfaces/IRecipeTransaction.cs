namespace Catalog.DAL.Interfaces;

public interface IRecipeTransaction
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken);
}
