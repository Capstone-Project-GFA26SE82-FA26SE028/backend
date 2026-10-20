namespace Ordering.DAL.Interfaces;

public interface IOrderingTransaction
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken ct);
}
