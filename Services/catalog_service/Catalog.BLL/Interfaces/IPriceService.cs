using Catalog.Common.DTOs;
using Shared.Common.DTOs;

namespace Catalog.BLL.Interfaces;

public interface IPriceService
{
    Task<ResponseDTO> GetAll(PriceQueryDTO query, CancellationToken cancellationToken);

    Task<ResponseDTO> GetById(Guid id, CancellationToken cancellationToken);

    Task<ResponseDTO> Create(CreatePriceDTO request, CancellationToken cancellationToken);

    Task<ResponseDTO> Update(Guid id, UpdatePriceDTO request, CancellationToken cancellationToken);

    Task<ResponseDTO> Delete(Guid id, CancellationToken cancellationToken);

    Task<ResponseDTO> Replace(Guid id, ReplacePriceDTO request, CancellationToken cancellationToken);

    Task<ResponseDTO> GetAppliedPrice(Guid itemId, string? channel, string? at, CancellationToken cancellationToken);
}
