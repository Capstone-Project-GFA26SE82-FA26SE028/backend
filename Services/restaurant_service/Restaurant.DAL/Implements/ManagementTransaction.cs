using Restaurant.DAL.Interfaces;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Restaurant.Common.Exceptions;
using Shared.Persistence.Interfaces;

namespace Restaurant.DAL.Implements;

public sealed class ManagementTransaction(IUnitOfWork unitOfWork) : IManagementTransaction
{
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(action, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ManagementException(409, "The data has changed. Reload before continuing.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && sql.Number is 2601 or 2627 or 547 or 1205)
        {
            throw new ManagementException(409, "The data already exists or is in use.");
        }
        catch (SqlException ex) when (ex.Number == 1205)
        {
            throw new ManagementException(409, "A concurrent operation was detected. Reload and try again.");
        }
    }
}
