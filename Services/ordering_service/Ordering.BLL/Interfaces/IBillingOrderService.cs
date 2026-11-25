using Ordering.Common.DTOs;

namespace Ordering.BLL.Interfaces;

public interface IBillingOrderService
{
    Task<IReadOnlyList<OrderReportSnapshotDTO>> GetReportItems(Guid restaurantId, Guid[] orderIds, CancellationToken ct);

    Task<IReadOnlyList<OrderReportChannelDTO>> GetReportChannels(Guid restaurantId, Guid[] orderIds, CancellationToken ct);

    Task<BillingOrderSnapshotDTO> GetBySession(Guid sessionId, CancellationToken ct);
    Task<BillingOrderSnapshotDTO> GetForRestaurant(Guid sessionId, Guid restaurantId, CancellationToken ct);
}
