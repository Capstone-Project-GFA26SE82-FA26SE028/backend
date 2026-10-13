using Shared.Common.DTOs;
using Identity.Common.DTOs;

namespace Identity.BLL.Interfaces;

public interface IAuditLogService
{
    Task<ResponseDTO> GetAll(Guid actorId, int page, int pageSize, Guid? userId, CancellationToken ct);
}
