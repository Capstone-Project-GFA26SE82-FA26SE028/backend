using Customer.DAL.Interfaces;
using Shared.Persistence.Interfaces;

namespace Customer.DAL.Implements;

public sealed class CustomerTransaction(IUnitOfWork unitOfWork) : ICustomerTransaction
{
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        return await unitOfWork.ExecuteInTransactionAsync(action, ct);
    }
}

