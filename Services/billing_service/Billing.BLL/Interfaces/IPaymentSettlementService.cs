namespace Billing.BLL.Interfaces;

public interface IPaymentSettlementService
{
    Task<Guid?> GetVerifiedCheckout(Guid restaurantId, Guid sessionId, CancellationToken ct);
}
