using Restaurant.Common.DTOs;

namespace Restaurant.BLL.Interfaces;

public interface ITableSessionService
{
    Task<bool> CloseAfterPayment(Guid restaurantId, Guid sessionId, CancellationToken ct);

    Task<TableSessionScanDto> OpenForWaiter(Guid tableId, CancellationToken ct);

    Task<OrderSessionDto> GetForOrdering(Guid sessionId, Guid? tableId, string guestToken, CancellationToken ct);
    Task<OrderSessionDto> GetForServing(Guid sessionId, CancellationToken ct);

    Task<TableSessionScanDto> Scan(string token, CancellationToken ct);
    Task<bool> CloseEmptySession(Guid id, CancellationToken ct);
}
