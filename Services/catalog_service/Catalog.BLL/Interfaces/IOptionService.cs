using Catalog.Common.DTOs;
using Shared.Common.DTOs;

namespace Catalog.BLL.Interfaces;

public interface IOptionService
{
    Task<ResponseDTO> Create(CreateOptionDTO request, CancellationToken cancellationToken);

    Task<ResponseDTO> GetAll(OptionQueryDTO query, CancellationToken cancellationToken);

    Task<ResponseDTO> GetById(Guid id, CancellationToken cancellationToken);

    Task<ResponseDTO> Update(Guid id, UpdateOptionDTO request, CancellationToken cancellationToken);

    Task<ResponseDTO> Delete(Guid id, CancellationToken cancellationToken);

    Task<ResponseDTO> SetStatus(Guid id, SetOptionStatusDTO request, CancellationToken cancellationToken);
}
