using Catalog.Common.DTOs;
using Shared.Common.DTOs;

namespace Catalog.BLL.Interfaces;

public interface ICategoryService
{
    Task<ResponseDTO> GetAll(
        CategoryQueryDTO query,
        CancellationToken cancellationToken);

    Task<ResponseDTO> GetById(
        Guid id,
        CancellationToken cancellationToken);

    Task<ResponseDTO> Create(
        CreateCategoryDTO request,
        CancellationToken cancellationToken);

    Task<ResponseDTO> Update(
        Guid id,
        UpdateCategoryDTO request,
        CancellationToken cancellationToken);

    Task<ResponseDTO> SetStatus(
        Guid id,
        SetCategoryStatusDTO request,
        CancellationToken cancellationToken);

    Task<ResponseDTO> Delete(
        Guid id,
        CancellationToken cancellationToken);
}
