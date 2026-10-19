using System.Data;
using Catalog.Common.Exceptions;
using Catalog.DAL.Interfaces;
using Catalog.DAL.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shared.Persistence.Interfaces;

namespace Catalog.DAL.Implements;

public sealed class PriceTransaction(CatalogDbContext context, IUnitOfWork unitOfWork) : IPriceTransaction
{
    public async Task<T> ExecuteAsync<T>(Guid itemId, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT ItemId FROM dbo.Item WITH (UPDLOCK, HOLDLOCK) WHERE ItemId = {itemId}",
                    cancellationToken);
                return await action();
            }, IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PricePersistenceException(PricePersistenceFailure.Conflict, exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException sqlException)
        {
            throw new PricePersistenceException(Classify(sqlException), exception);
        }
        catch (SqlException exception)
        {
            throw new PricePersistenceException(Classify(exception), exception);
        }
    }

    private static PricePersistenceFailure Classify(SqlException exception)
    {
        return exception.Number switch
        {
            2601 or 2627 => PricePersistenceFailure.Conflict,
            547 => PricePersistenceFailure.RelatedDataChanged,
            1205 => PricePersistenceFailure.Busy,
            _ => PricePersistenceFailure.Unavailable
        };
    }
}
