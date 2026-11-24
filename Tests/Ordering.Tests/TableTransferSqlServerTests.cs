using Microsoft.EntityFrameworkCore;
using Ordering.BLL.Implements;
using Ordering.DAL.Implements;
using Shared.Common.TableTransfers;
using Shared.Persistence.UnitOfWorks;
using Ordering.DAL.Models;
using Xunit;

namespace Ordering.Tests;

public sealed partial class CancellationRequestSqlServerTests
{
    [CancellationSqlFact]
    public async Task TransferSqlConcurrentReplayPreservesQuantityAndMovesOnce()
    {
        var remote = new TableTransferTests.Sessions(session, restaurant, waiter);
        var request = new TableTransferDTO
        {
            OperationId = Guid.NewGuid(),
            SourceSessionId = session,
            TargetTableId = Guid.NewGuid(),
            Mode = "Split",
            Reason = "Tách một phần",
            Items = [new TableTransferLineDTO
            {
                OrderItemId = itemId,
                Quantity = 1
            }]
        };
        await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var db = Db();
            var service = new TableTransferService(new TableTransferRepository(db), new OrderRepository(db),
                new CheckoutLockRepository(db), new OrderingTransaction(new UnitOfWork<OrderingDbContext>(db)),
                remote, TimeProvider.System);
            await service.Execute(request, default);
        }));
        await using var final = Db();
        Assert.Single(await final.TableTransfers.ToListAsync());
        Assert.Equal("Completed", (await final.TableTransfers.SingleAsync()).Status);
        Assert.Equal(2, await final.OrderItems.CountAsync());
        Assert.Equal(2, await final.OrderItems.SumAsync(value => value.Quantity));
        Assert.Equal(2, await final.KitchenTasks.SumAsync(value => value.Quantity));
        Assert.Single(await final.Orders.Where(value => value.TableSessionRef == remote.Target).ToListAsync());
    }
}
