namespace Customer.BLL.Interfaces;

public interface ILoyaltyManagerAccess
{
    Task EnsureManagerAccessAsync(Guid restaurantId, CancellationToken ct);
}
