namespace Billing.BLL.Interfaces;

public interface ICustomerLoyaltyClient
{
    Task EarnPointsAsync(Guid customerId, Guid billId, decimal finalAmount, CancellationToken ct);
    Task CommitPointsAsync(Guid billId, CancellationToken ct);
}
