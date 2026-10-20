using Catalog.Common.DTOs;
using Shared.Common.DTOs;

namespace Catalog.BLL.Interfaces;

public interface IMenuService
{
    Task<ResponseDTO> GetAll(MenuQueryDTO query, Guid? tableSessionId, string? guestToken, CancellationToken cancellationToken);

    Task<ResponseDTO> GetById(Guid id, string channel, Guid? tableSessionId, string? guestToken, CancellationToken cancellationToken);
}
