using Restaurant.Common.DTOs;

namespace Restaurant.BLL.Interfaces;

public interface IAreaService
{
    Task<PageResult<AreaDto>> GetAll(ListQuery query, CancellationToken ct);

    Task<AreaDto> GetById(Guid id, CancellationToken ct);

    Task<AreaDto> Create(AreaWriteDto body, CancellationToken ct);

    Task<AreaDto> Update(Guid id, AreaWriteDto body, CancellationToken ct);

    Task<AreaDto> SetStatus(Guid id, bool active, CancellationToken ct);

    Task Delete(Guid id, CancellationToken ct);
}
