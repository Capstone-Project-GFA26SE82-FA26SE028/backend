using System.Data;
using Catalog.Common.Exceptions;
using Catalog.DAL.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shared.Persistence.Interfaces;

namespace Catalog.DAL.Implements;

public sealed class RecipeTransaction(IUnitOfWork unitOfWork) : IRecipeTransaction
{
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(action, IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new RecipePersistenceException(true, exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException sqlException)
        {
            throw new RecipePersistenceException(IsConflict(sqlException), exception);
        }
        catch (SqlException exception)
        {
            throw new RecipePersistenceException(IsConflict(exception), exception);
        }
    }

    private static bool IsConflict(SqlException exception)
    {
        return exception.Number is 1205 or 2601 or 2627 or 547;
    }
}
