using Shared.Common.Billing;

namespace Restaurant.BLL.Interfaces;

public interface ISessionPaymentClient
{
    Task<PaidSessionSettlement?> GetPaidAsync(Guid sessionId, Guid restaurantId, CancellationToken ct);
}
