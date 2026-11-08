using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shared.Common.DTOs;
using Shared.Persistence.Interfaces;

namespace Catalog.BLL.Implements;

public sealed class OptionWriteExecutor(IUnitOfWork unitOfWork)
{
    public async Task<ResponseDTO> ExecuteAsync(Func<Task<ResponseDTO>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(action, IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new ResponseDTO("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.", 409);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException sqlException)
        {
            return FromSql(sqlException);
        }
        catch (SqlException exception)
        {
            return FromSql(exception);
        }
    }

    private static ResponseDTO FromSql(SqlException exception)
    {
        return exception.Number switch
        {
            2601 or 2627 => new ResponseDTO("Dữ liệu tùy chọn đã tồn tại.", 409),
            547 => new ResponseDTO("Dữ liệu liên quan đã thay đổi. Vui lòng tải lại và thử lại.", 409),
            1205 => new ResponseDTO("Phát hiện thao tác đồng thời. Vui lòng thử lại.", 409),
            _ => new ResponseDTO("Kho dữ liệu tùy chọn hiện không khả dụng.", 503)
        };
    }
}
