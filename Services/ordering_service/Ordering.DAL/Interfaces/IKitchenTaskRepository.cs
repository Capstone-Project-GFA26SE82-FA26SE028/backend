using Ordering.DAL.Models;

namespace Ordering.DAL.Interfaces;

public interface IKitchenTaskRepository
{
    Task<Guid?> GetOnlyActiveStationAsync(CancellationToken ct);

    Task<bool> IsStationActiveAsync(Guid stationId, CancellationToken ct);

    void Create(KitchenTask task);

    Task<(List<KitchenTask> Items, int Total)> GetPageAsync(
        int page,
        int pageSize,
        Guid? stationId,
        string? status,
        Guid? orderId,
        Guid? sessionId,
        CancellationToken ct);

    Task<KitchenTask?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<KitchenTask?> GetByIdForUpdateAsync(Guid id, CancellationToken ct);

    Task<(int Pending, int Cooking, int Ready, double AvgCookingMinutes)> GetSummaryAsync(
        Guid? stationId,
        CancellationToken ct);

    Task<List<KitchenTask>> GetPendingForBatchesAsync(Guid? stationId, CancellationToken ct);

    Task<List<KitchenTask>> GetByIdsForUpdateAsync(IEnumerable<Guid> ids, CancellationToken ct);

    Task<List<KitchenTask>> GetHoldTasksAsync(Guid sessionId, Guid? orderId, CancellationToken ct);
}
