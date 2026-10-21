using Ordering.DAL.Models;

namespace Ordering.DAL.Interfaces;

public interface IKitchenTaskRepository
{
    Task<Guid?> GetOnlyActiveStationAsync(CancellationToken ct);

    Task<bool> IsStationActiveAsync(Guid stationId, CancellationToken ct);

    void Create(KitchenTask task);
}
