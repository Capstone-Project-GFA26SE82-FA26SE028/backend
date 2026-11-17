namespace Ordering.BLL.Interfaces;

public interface IKitchenStationAccess
{
    Task<IReadOnlyList<Guid>> GetOnlineRestaurantIdsAsync(CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<Guid>>([]);
    }

    Task RequireAsync(bool write, CancellationToken ct);

    Task<Guid> RequireKitchenTaskAccessAsync(CancellationToken ct);
}
