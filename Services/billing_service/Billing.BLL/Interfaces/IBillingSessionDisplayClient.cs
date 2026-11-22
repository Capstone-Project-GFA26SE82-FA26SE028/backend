using Billing.Common.DTOs;

namespace Billing.BLL.Interfaces;

public interface IBillingSessionDisplayClient
{
    Task<IReadOnlyDictionary<Guid, BillingSessionDisplayDTO>> GetByIdsAsync(Guid restaurantId,
        IReadOnlyList<Guid> sessionIds, CancellationToken ct);
}
