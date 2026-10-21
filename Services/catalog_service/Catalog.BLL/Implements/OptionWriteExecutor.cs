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
            return new ResponseDTO("The data has changed. Reload and try again.", 409);
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
            2601 or 2627 => new ResponseDTO("The option data already exists.", 409),
            547 => new ResponseDTO("Related data has changed. Reload and try again.", 409),
            1205 => new ResponseDTO("A concurrent operation was detected. Try again.", 409),
            _ => new ResponseDTO("The option data store is unavailable.", 503)
        };
    }
}
