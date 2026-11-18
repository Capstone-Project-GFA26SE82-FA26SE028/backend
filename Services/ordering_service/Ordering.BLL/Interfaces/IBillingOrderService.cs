using Ordering.Common.DTOs;

namespace Ordering.BLL.Interfaces;

public interface IBillingOrderService
{
    Task<BillingOrderSnapshotDTO> GetBySession(Guid sessionId, CancellationToken ct);
    Task<BillingOrderSnapshotDTO> GetForRestaurant(Guid sessionId, Guid restaurantId, CancellationToken ct);
}
