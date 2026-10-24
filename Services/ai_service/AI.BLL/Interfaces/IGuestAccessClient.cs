using AI.Common.DTOs;

namespace AI.BLL.Interfaces;

public interface IGuestAccessClient
{
    Task<VerifiedGuest> VerifyAsync(GuestContext guest, CancellationToken ct);
}
