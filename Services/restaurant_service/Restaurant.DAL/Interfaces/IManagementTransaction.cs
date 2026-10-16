namespace Restaurant.DAL.Interfaces;

public interface IManagementTransaction
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken ct);
}
