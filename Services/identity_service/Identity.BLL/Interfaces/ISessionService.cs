using Shared.Common.DTOs;
using Identity.Common.DTOs;

namespace Identity.BLL.Interfaces;

public interface ISessionService
{
    Task<ResponseDTO> GetMe(Guid userId, int page, int pageSize, CancellationToken ct);
    Task<ResponseDTO> Delete(Guid userId, Guid sessionId, CancellationToken ct);
    Task<ResponseDTO> DeleteAll(Guid userId, CancellationToken ct);
    Task<ResponseDTO> GetAll(Guid actorId, int page, int pageSize, Guid? userId, CancellationToken ct, Identity.Common.DTOs.SessionFilter? filter = null);
}
