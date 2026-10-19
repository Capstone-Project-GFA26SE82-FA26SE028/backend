using System.Data;
using Catalog.Common.Exceptions;
using Catalog.DAL.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shared.Persistence.Interfaces;

namespace Catalog.DAL.Implements;

public sealed class ItemTransaction(IUnitOfWork unitOfWork) : IItemTransaction
{
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(
                action, IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ItemPersistenceException(ItemPersistenceFailure.ConcurrentUpdate, exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException sqlException)
        {
            throw new ItemPersistenceException(Classify(sqlException), exception);
        }
        catch (SqlException exception)
        {
            throw new ItemPersistenceException(Classify(exception), exception);
        }
    }

    private static ItemPersistenceFailure Classify(SqlException exception)
    {
        return exception.Number switch
        {
            2601 or 2627 => ItemPersistenceFailure.Duplicate,
            547 => ItemPersistenceFailure.RelatedDataChanged,
            1205 => ItemPersistenceFailure.Busy,
            _ => ItemPersistenceFailure.Unavailable
        };
    }
}
