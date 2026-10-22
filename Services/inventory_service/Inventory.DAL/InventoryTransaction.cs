using System.Data;
using Inventory.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shared.Persistence.Interfaces;

namespace Inventory.DAL;

public sealed class InventoryTransaction(IUnitOfWork unitOfWork)
{
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(action, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InventoryException(409, "Stock changed. Reload and retry with the same operation ID.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException sql)
        {
            throw Failure(sql);
        }
        catch (SqlException exception)
        {
            throw Failure(exception);
        }
    }

    private static InventoryException Failure(SqlException exception)
    {
        return new InventoryException(exception.Number is 1205 or 2601 or 2627 or 547 ? 409 : 503,
            "Inventory is busy or unavailable. Retry with the same operation ID.");
    }
}
