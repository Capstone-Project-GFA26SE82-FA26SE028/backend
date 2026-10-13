using Identity.Common.DTOs;
using Shared.Common.DTOs;

namespace Identity.BLL.Interfaces;

public interface IRoleService
{
    Task<ResponseDTO> GetAll(Guid actorId, CancellationToken ct);
    Task<ResponseDTO> GetById(Guid actorId, string id, CancellationToken ct);
    Task<ResponseDTO> Create(Guid actorId, RoleDTO request, CancellationToken ct);
    Task<ResponseDTO> Update(Guid actorId, string id, RoleDTO request, CancellationToken ct);
    Task<ResponseDTO> Delete(Guid actorId, string id, CancellationToken ct);
}

