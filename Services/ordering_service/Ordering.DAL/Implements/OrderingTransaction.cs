using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Ordering.Common.Exceptions;
using Ordering.DAL.Interfaces;
using Shared.Persistence.Interfaces;

namespace Ordering.DAL.Implements;

public sealed class OrderingTransaction(IUnitOfWork unitOfWork) : IOrderingTransaction
{
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(action, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new OrderingException(409, "Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");
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

    private static OrderingException Failure(SqlException exception)
    {
        return new OrderingException(exception.Number is 1205 or 2601 or 2627 or 547 ? 409 : 503,
            "Dữ liệu đặt món đang bận hoặc không khả dụng. Vui lòng thử lại với cùng khóa chống trùng lặp.");
    }
}
