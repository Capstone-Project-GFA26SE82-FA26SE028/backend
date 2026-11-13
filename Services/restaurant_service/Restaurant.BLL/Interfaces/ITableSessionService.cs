using Restaurant.Common.DTOs;

namespace Restaurant.BLL.Interfaces;

public interface ITableSessionService
{
    Task<TableSessionClosureDto> ClosePaidSession(Shared.Common.Billing.PaidSessionSettlement payment, CancellationToken ct);

    Task<bool> TryClosePaidSession(Guid sessionId, CancellationToken ct);

    Task<TableSessionScanDto> OpenForWaiter(Guid tableId, CancellationToken ct);

    Task<OrderSessionDto> GetForOrdering(Guid sessionId, Guid? tableId, string guestToken, CancellationToken ct);

    Task<TableSessionScanDto> Scan(string token, CancellationToken ct);
}
