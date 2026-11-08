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
            throw new ManagementException(409, "Dữ liệu đã thay đổi trong quá trình thực hiện. Vui lòng tải lại và thử lại.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException sql && sql.Number is 2601 or 2627 or 547 or 1205)
        {
            throw new ManagementException(409, "Yêu cầu đặt bàn hoặc phân công xung đột với dữ liệu hiện có. Vui lòng tải lại và thử lại.");
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            throw new ManagementException(409, "Một thao tác đồng thời xung đột với yêu cầu này. Vui lòng tải lại và thử lại.");
        }
    }
}
