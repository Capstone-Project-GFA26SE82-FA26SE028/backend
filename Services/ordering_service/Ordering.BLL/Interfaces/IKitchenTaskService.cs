using Ordering.Common.DTOs;

namespace Ordering.BLL.Interfaces;

public interface IKitchenTaskService
{
    Task<KitchenTaskPageDTO> GetAll(
        int page,
        int pageSize,
        Guid? stationId,
        string? status,
        Guid? orderId,
        Guid? sessionId,
        CancellationToken ct);

    Task<KitchenTaskDTO> GetById(Guid id, CancellationToken ct);

    Task<KitchenTaskDTO> SetStatus(Guid id, string status, string? version, CancellationToken ct);

    Task<KitchenTaskDTO> SetPriority(Guid id, int priority, string? version, CancellationToken ct);

    Task<KitchenSummaryDTO> GetSummary(Guid? stationId, CancellationToken ct);
}

