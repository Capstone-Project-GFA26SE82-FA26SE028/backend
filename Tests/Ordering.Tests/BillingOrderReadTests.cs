using Ordering.BLL.Implements;
using Ordering.BLL.Interfaces;
using Ordering.Common.Exceptions;
using Ordering.DAL.Interfaces;
using Ordering.DAL.Models;
using Xunit;

namespace Ordering.Tests;

public sealed class BillingOrderReadTests
{
    [Fact]
    public async Task ReadDeductsCancelledQuantitiesPreservesSnapshotsAndExcludesPendingOrders()
    {
        var repository = new Orders();
        var confirmed = Order("Confirmed", "Ready", 3, 1);
        var cancelled = Order("Confirmed", "Cancelled", 2, 0);
        var pending = Order("PendingConfirmation", "PendingConfirmation", 1, 0);
        repository.Values.AddRange([confirmed, cancelled, pending]);
        var result = await new BillingOrderService(repository, new Session()).GetBySession(Guid.NewGuid(), default);
        var line = Assert.Single(result.Items);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(45000.25m, line.UnitPrice);
        Assert.Equal(confirmed.OrderItems.Single().OriginalTableRef, line.OriginalTableId);
        Assert.Equal(1000, Assert.Single(line.Options).ExtraPrice);
        Assert.Equal(1, result.PendingOrderCount);
        Assert.Equal(3, confirmed.OrderItems.Single().Quantity);
        Assert.Equal(0, repository.Writes);
    }

    [Fact]
    public async Task MissingOriginalTableIsReportedWithoutSubstitutingCurrentTable()
    {
        var repository = new Orders();
        var order = Order("Confirmed", "Served", 1, 0);
        order.OrderItems.Single().OriginalTableRef = null;
        repository.Values.Add(order);
        var error = await Assert.ThrowsAsync<OrderingException>(() =>
            new BillingOrderService(repository, new Session()).GetBySession(Guid.NewGuid(), default));
        Assert.Equal(409, error.StatusCode);
        Assert.Null(order.OrderItems.Single().OriginalTableRef);
    }

    [Fact]
    public async Task InvalidCancelledQuantityIsReported()
    {
        var repository = new Orders();
        repository.Values.Add(Order("Confirmed", "Served", 1, 2));
        var error = await Assert.ThrowsAsync<OrderingException>(() =>
            new BillingOrderService(repository, new Session()).GetBySession(Guid.NewGuid(), default));
        Assert.Equal(409, error.StatusCode);
    }

    private static Order Order(string orderStatus, string lineStatus, int quantity, int cancelled)
    {
        var order = new Order
        {
            OrderId = Guid.NewGuid(),
            Status = orderStatus
        };
        order.OrderItems.Add(new OrderItem
        {
            OrderItemId = Guid.NewGuid(),
            ItemRef = Guid.NewGuid(),
            OriginalTableRef = Guid.NewGuid(),
            NameSnapshot = "Test dish",
            UnitPriceSnapshot = 45000.25m,
            Quantity = quantity,
            CancelledQuantity = cancelled,
            Status = lineStatus,
            OrderItemOptions =
            [
                new OrderItemOption
                {
                    OptionRef = Guid.NewGuid(),
                    NameSnapshot = "Extra",
                    ExtraPriceSnapshot = 1000,
                    Quantity = 2
                }
            ]
        });
        return order;
    }

    private sealed class Session : IBillingSessionClient
    {
        public Task<Guid> GetRestaurantIdAsync(Guid sessionId, CancellationToken ct)
        {
            return Task.FromResult(Guid.NewGuid());
        }
    }

    private sealed class Orders : IOrderRepository
    {
        public List<Order> Values { get; } = [];
        public int Writes { get; private set; }

        public Task<List<Order>> GetForBillingAsync(Guid sessionId, CancellationToken ct)
        {
            return Task.FromResult(Values);
        }

        public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<Order?> GetByKeyAsync(string key, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<List<Order>> GetAllAsync(Guid sessionId, string? guestScope, int page, int pageSize, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public void Create(Order order)
        {
            Writes++;
        }

        public void SetOriginalVersion(Order order, byte[] version)
        {
            Writes++;
        }
    }
}
