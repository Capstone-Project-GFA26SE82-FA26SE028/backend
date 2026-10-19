using Restaurant.Common.DTOs;

namespace Restaurant.BLL.Interfaces;

public interface IGuestAccessService
{
    Task<GuestAccessDto> Join(Guid sessionId, string phoneOrCode, CancellationToken ct);
    Task<GuestAccessDto> GetMe(Guid sessionId, string token, CancellationToken ct);
}
