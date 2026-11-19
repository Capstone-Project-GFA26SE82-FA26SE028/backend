using Ordering.DAL.Models;

namespace Ordering.DAL.Interfaces;

public interface IKitchenAssignmentRepository
{
    Task<bool> ExistsForUserAsync(Guid userId, CancellationToken ct);
    Task LockUserAsync(Guid userId, CancellationToken ct);
    Task<KitchenStation?> LockStationAsync(Guid stationId, CancellationToken ct);
    Task<KitchenAssignment?> GetByIdAsync(Guid stationId, CancellationToken ct);
    Task<(List<KitchenAssignment> Items, int Total)> GetAllAsync(int page, int pageSize, CancellationToken ct);
    Task<bool> HasCookingTasksAsync(Guid stationId, CancellationToken ct);
    Task<bool> HasPendingTasksAsync(Guid stationId, CancellationToken ct);
    void Create(KitchenAssignment assignment);
    void Delete(KitchenAssignment assignment);
}
