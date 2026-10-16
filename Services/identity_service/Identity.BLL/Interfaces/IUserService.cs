using Shared.Common.DTOs;
using Identity.Common.DTOs;
namespace Identity.BLL.Interfaces;

public interface IUserService
{
    Task<ResponseDTO> Update(Guid actorId, Guid userId, UpdateUserDTO request, CancellationToken ct);
    Task<ResponseDTO> Create(Guid actorId, CreateUserDTO request, CancellationToken ct);
    Task<ResponseDTO> SetRole(Guid actorId, Guid userId, string roleId, CancellationToken ct);
    Task<ResponseDTO> GetMe(Guid actorId, CancellationToken ct);
    Task<ResponseDTO> UpdateMe(Guid actorId, UpdateProfileDTO request, CancellationToken ct);
    Task<ResponseDTO> GetAll(Guid actorId, int page, int pageSize, string? search, CancellationToken ct, Identity.Common.DTOs.UserFilter? filter = null);
    Task<ResponseDTO> GetById(Guid actorId, Guid userId, CancellationToken ct);
    Task<ResponseDTO> SetStatus(Guid actorId, Guid userId, bool isActive, CancellationToken ct);
}
