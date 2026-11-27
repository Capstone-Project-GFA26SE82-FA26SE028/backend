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
    public async Task ReportChannelsRequireManagerBeforeReadingOrders()
    {
        var repository = new Orders();
        var service = new BillingOrderService(repository, new Session(), new DeniedReportAccess());
        await Assert.ThrowsAsync<OrderingException>(() => service.GetReportChannels(Guid.NewGuid(), [Guid.NewGuid()], default));
        Assert.Equal(0, repository.ReportReads);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task ReportChannelBatchMustBeBounded(int count)
    {
        var repository = new Orders();
        var service = new BillingOrderService(repository, new Session(), new AllowOrderReportAccess());
        var error = await Assert.ThrowsAsync<OrderingException>(() => service.GetReportChannels(Guid.NewGuid(),
            Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray(), default));
        Assert.Equal(400, error.StatusCode);
        Assert.Equal(0, repository.ReportReads);
    }

    private sealed class DeniedReportAccess : IOrderReportAccess
    {
        public Task RequireManager(Guid restaurantId, CancellationToken ct)
        {
            throw new OrderingException(403, "Denied");
        }
    }

    [Fact]
    public async Task ReportChannelsRetainsTakeawayInsideSessionAndExcludesOtherRestaurants()
    {
        var repository = new Orders();
        var restaurant = Guid.NewGuid();
        var item = Order("Confirmed", "Ready", 1, 0);
        item.RestaurantRef = restaurant;
        item.TableSessionRef = Guid.NewGuid();
        item.Channel = "Takeaway";
        var other = Order("Confirmed", "Ready", 1, 0);
        other.RestaurantRef = Guid.NewGuid();
        other.Channel = "DineIn";
        repository.Values.AddRange([item, other]);
        var service = new BillingOrderService(repository, new Session(), new AllowOrderReportAccess());
        var result = await service.GetReportChannels(restaurant, [item.OrderId, other.OrderId, Guid.NewGuid()], default);
        var channel = Assert.Single(result);
        Assert.Equal(item.OrderId, channel.OrderId);
        Assert.Equal("Takeaway", channel.Channel);
        Assert.Equal(0, repository.Writes);
    }

    [Fact]
    public async Task ReadDeductsCancelledQuantitiesPreservesSnapshotsAndExcludesPendingOrders()
    {
        var repository = new Orders();
        var confirmed = Order("Confirmed", "Ready", 3, 1);
        var cancelled = Order("Confirmed", "Cancelled", 2, 0);
        var pending = Order("PendingConfirmation", "PendingConfirmation", 1, 0);
        repository.Values.AddRange([confirmed, cancelled, pending]);
        var result = await new BillingOrderService(repository, new Session(), new AllowOrderReportAccess()).GetBySession(Guid.NewGuid(), default);
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
            new BillingOrderService(repository, new Session(), new AllowOrderReportAccess()).GetBySession(Guid.NewGuid(), default));
        Assert.Equal(409, error.StatusCode);
        Assert.Null(order.OrderItems.Single().OriginalTableRef);
    }

    [Fact]
    public async Task InvalidCancelledQuantityIsReported()
    {
        var repository = new Orders();
        repository.Values.Add(Order("Confirmed", "Served", 1, 2));
        var error = await Assert.ThrowsAsync<OrderingException>(() =>
            new BillingOrderService(repository, new Session(), new AllowOrderReportAccess()).GetBySession(Guid.NewGuid(), default));
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
        public int ReportReads { get; private set; }

        public Task<List<Ordering.Common.DTOs.OrderReportSnapshotDTO>> GetReportItemsAsync(Guid restaurantId, Guid[] orderIds, CancellationToken ct)
        {
            return Task.FromResult(Values.Where(value => value.RestaurantRef == restaurantId && orderIds.Contains(value.OrderId))
                .Select(value => new Ordering.Common.DTOs.OrderReportSnapshotDTO(value.OrderId, value.Channel, value.Status, [])).ToList());
        }

        public Task<List<Ordering.Common.DTOs.OrderReportChannelDTO>> GetReportChannelsAsync(Guid restaurantId, Guid[] orderIds, CancellationToken ct)
        {
            ReportReads++;
            return Task.FromResult(Values.Where(value => value.RestaurantRef == restaurantId && orderIds.Contains(value.OrderId))
                .Select(value => new Ordering.Common.DTOs.OrderReportChannelDTO(value.OrderId, value.Channel)).ToList());
        }

        public Task<List<Order>> GetForTransferAsync(Guid sessionId, CancellationToken ct)
        {
            return Task.FromResult(Values);
        }

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

        public Task<List<Order>> GetTakeawayAsync(Guid restaurantId, Guid? customerId, string? status, int page, int pageSize, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<(List<Order> Items, int TotalCount)> GetCustomerTakeawayAsync(Guid customerId, string? status,
            int page, int pageSize, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<List<Order>> GetOnlinePendingAsync(CancellationToken ct)
        {
            return Task.FromResult(new List<Order>());
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
