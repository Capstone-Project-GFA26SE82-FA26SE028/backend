namespace Ordering.BLL.Interfaces;

public sealed record TakeawayActor(Guid UserId, bool IsCustomer, string RoleId);

public interface ITakeawayAccess
{
    Task<TakeawayActor> GetActorAsync(Guid restaurantId, CancellationToken ct);
}
