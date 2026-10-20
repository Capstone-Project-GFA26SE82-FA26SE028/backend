using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Restaurant.Common.Exceptions;
using Restaurant.DAL.Interfaces;
using Shared.Persistence.Interfaces;

namespace Restaurant.DAL.Implements;

public sealed class ReservationTransaction(IUnitOfWork unitOfWork) : IReservationTransaction
{
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(action, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ManagementException(409, "The data changed during this operation. Reload and try again.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException sql && sql.Number is 2601 or 2627 or 547 or 1205)
        {
            throw new ManagementException(409, "The reservation or assignment conflicts with existing data. Reload and try again.");
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            throw new ManagementException(409, "A concurrent operation conflicted with this request. Reload and try again.");
        }
    }
}
