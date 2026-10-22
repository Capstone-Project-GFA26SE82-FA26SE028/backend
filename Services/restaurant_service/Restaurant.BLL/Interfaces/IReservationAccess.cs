namespace Restaurant.BLL.Interfaces;

public interface IReservationAccess
{
    Task<IReadOnlyList<Guid>?> GetReadScopeAsync(CancellationToken ct);
    Task RequireReadAsync(Guid restaurantId, CancellationToken ct);
    Task<Guid> RequireMutationAsync(Guid restaurantId, CancellationToken ct);
}
