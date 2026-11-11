using Restaurant.Common.DTOs;

namespace Restaurant.BLL.Interfaces;

public interface ITableService
{
    Task<TableDto> ConfirmClean(Guid id, string version, CancellationToken ct);

    Task<PageResult<ServiceTableDto>> GetServiceTables(ListQuery query, CancellationToken ct);

    Task<PageResult<TableDto>> GetAll(ListQuery query, CancellationToken ct);

    Task<TableDto> GetById(Guid id, CancellationToken ct);

    Task<TableDto> Create(TableWriteDto body, CancellationToken ct);

    Task<TableDto> Update(Guid id, TableWriteDto body, CancellationToken ct);

    Task<TableDto> AssignArea(Guid id, AssignAreaDto body, CancellationToken ct);

    Task<TableDto> SetStatus(Guid id, StatusDto body, CancellationToken ct);

    Task Delete(Guid id, string version, CancellationToken ct);
}
