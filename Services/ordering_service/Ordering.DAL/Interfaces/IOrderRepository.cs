using Ordering.DAL.Models;
using Ordering.Common.DTOs;

namespace Ordering.DAL.Interfaces;

public interface IOrderRepository
{
    Task<List<OrderReportSnapshotDTO>> GetReportItemsAsync(Guid restaurantId, Guid[] orderIds, CancellationToken ct);

    Task<List<OrderReportChannelDTO>> GetReportChannelsAsync(Guid restaurantId, Guid[] orderIds, CancellationToken ct);

    Task<List<Order>> GetForBillingAsync(Guid sessionId, CancellationToken ct);

    Task<Order?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<Order?> GetByKeyAsync(string key, CancellationToken ct);

    Task<List<Order>> GetAllAsync(Guid sessionId, string? guestScope, int page, int pageSize, CancellationToken ct);

    Task<List<Order>> GetTakeawayAsync(Guid restaurantId, Guid? customerId, string? status, int page, int pageSize, CancellationToken ct);

    Task<List<Order>> GetOnlinePendingAsync(CancellationToken ct);

    void Create(Order order);

    void SetOriginalVersion(Order order, byte[] version);
}
