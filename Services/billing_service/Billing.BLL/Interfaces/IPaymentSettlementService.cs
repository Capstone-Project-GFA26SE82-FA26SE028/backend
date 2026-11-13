using Shared.Common.Billing;

namespace Billing.BLL.Interfaces;

public interface IPaymentSettlementService
{
    Task<PaidSessionSettlement> GetPaid(Guid sessionId, Guid restaurantId, CancellationToken ct);
    Task<bool> TryClose(Guid sessionId, Guid restaurantId, CancellationToken ct);
}
