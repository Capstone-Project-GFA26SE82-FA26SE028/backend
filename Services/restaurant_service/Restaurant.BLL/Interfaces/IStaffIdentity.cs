using Restaurant.Common.DTOs;

namespace Restaurant.BLL.Interfaces;

public interface IStaffIdentity
{
    Task<StaffActorDto> GetCurrentAsync(CancellationToken ct);
    Task<StaffActorDto> GetUserAsync(Guid userId, CancellationToken ct);
}
