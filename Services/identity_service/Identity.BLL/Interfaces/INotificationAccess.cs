namespace Identity.BLL.Interfaces;

public sealed record NotificationActor(Guid UserId, bool Customer, Guid? RestaurantId);

public interface INotificationAccess
{
    Task<NotificationActor> GetActor(Guid userId, CancellationToken ct);
    Task RequireAdmin(Guid userId, Guid restaurantId, CancellationToken ct);
}

public interface INotificationRestaurantAccess
{
    Task<bool> HasAssignmentAsync(Guid userId, Guid restaurantId, CancellationToken ct);
}

public interface INotificationAudience
{
    Task<IReadOnlySet<Guid>> GetStaffAsync(Guid restaurantId, CancellationToken ct);
}
