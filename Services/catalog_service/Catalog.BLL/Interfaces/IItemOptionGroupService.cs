using Catalog.Common.DTOs;
using Shared.Common.DTOs;

namespace Catalog.BLL.Interfaces;

public interface IItemOptionGroupService
{
    Task<ResponseDTO> Create(Guid itemId, CreateItemOptionGroupDTO request, CancellationToken cancellationToken);

    Task<ResponseDTO> GetAll(Guid itemId, CancellationToken cancellationToken);

    Task<ResponseDTO> GetById(Guid itemId, Guid groupId, CancellationToken cancellationToken);

    Task<ResponseDTO> Update(Guid itemId, Guid groupId, UpdateItemOptionGroupDTO request, CancellationToken cancellationToken);

    Task<ResponseDTO> Delete(Guid itemId, Guid groupId, CancellationToken cancellationToken);
}
