namespace Customer.DAL.Interfaces;

public interface ICustomerTransaction
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken ct);
}

