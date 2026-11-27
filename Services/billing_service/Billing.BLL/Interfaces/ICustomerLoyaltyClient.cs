namespace Billing.BLL.Interfaces;

public interface ICustomerLoyaltyClient
{
    Task<Guid> ResolveForBillAsync(Guid restaurantId, Guid? customerId, string? phoneNumber, CancellationToken ct);
    Task EarnPointsAsync(Guid customerId, Guid billId, decimal finalAmount, CancellationToken ct);
    Task CommitPointsAsync(Guid billId, CancellationToken ct);
}
