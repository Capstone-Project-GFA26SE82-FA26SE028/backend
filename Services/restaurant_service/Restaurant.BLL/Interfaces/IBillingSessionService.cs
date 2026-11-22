using Restaurant.Common.DTOs;

namespace Restaurant.BLL.Interfaces;

public interface IBillingSessionService
{
    Task<BillingSessionDto> GetById(Guid id, CancellationToken ct);
    Task<IReadOnlyList<BillingSessionDisplayDTO>> GetDisplays(Guid restaurantId, IReadOnlyList<Guid> ids, CancellationToken ct);
}
