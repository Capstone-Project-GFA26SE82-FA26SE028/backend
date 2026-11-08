using Catalog.Common.DTOs;
using Shared.Common.DTOs;

namespace Catalog.BLL.Interfaces;

public interface IItemService
{
    Task<ResponseDTO> SetKitchenStation(Guid id, SetItemKitchenStationDTO request, CancellationToken cancellationToken);

    Task<ResponseDTO> GetAll(ItemQueryDTO query, CancellationToken cancellationToken);

    Task<ResponseDTO> GetById(Guid id, CancellationToken cancellationToken);

    Task<ResponseDTO> Create(CreateItemDTO request, CancellationToken cancellationToken);

    Task<ResponseDTO> Update(Guid id, UpdateItemDTO request, CancellationToken cancellationToken);

    Task<ResponseDTO> SetStatus(Guid id, SetItemStatusDTO request, CancellationToken cancellationToken);
}
