using Restaurant.Common.DTOs;

namespace Restaurant.BLL.Interfaces;

public interface ITableSessionService
{
    Task<OrderSessionDto> GetForOrdering(Guid sessionId, Guid? tableId, string guestToken, CancellationToken ct);

    Task<TableSessionScanDto> Scan(string token, CancellationToken ct);
}
