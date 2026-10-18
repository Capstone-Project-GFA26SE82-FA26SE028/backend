using Restaurant.Common.DTOs;

namespace Restaurant.BLL.Interfaces;

public interface ITableSessionService
{
    Task<TableSessionScanDto> Scan(string token, CancellationToken ct);
}
