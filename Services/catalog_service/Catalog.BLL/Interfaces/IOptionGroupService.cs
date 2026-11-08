using Catalog.Common.DTOs;
using Shared.Common.DTOs;

namespace Catalog.BLL.Interfaces;

public interface IOptionGroupService
{
    Task<ResponseDTO> Create(CreateOptionGroupDTO request, CancellationToken cancellationToken);

    Task<ResponseDTO> GetAll(OptionGroupQueryDTO query, CancellationToken cancellationToken);

    Task<ResponseDTO> GetById(Guid id, CancellationToken cancellationToken);

    Task<ResponseDTO> Update(Guid id, UpdateOptionGroupDTO request, CancellationToken cancellationToken);

    Task<ResponseDTO> Delete(Guid id, CancellationToken cancellationToken);
}
