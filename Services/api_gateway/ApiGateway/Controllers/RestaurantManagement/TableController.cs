using ApiGateway.Swagger;
using Backend.Contracts.Restaurant;
using Microsoft.AspNetCore.Mvc;
using ApiGateway.DTOs.Restaurant;

namespace ApiGateway.Controllers.RestaurantManagement;

[ApiController]
[Route("api/tables")]
[RequireBearerToken]
public sealed class TableController(TableGrpc.TableGrpcClient client) : ManagementControllerBase
{
    [HttpPost("{id:guid}/confirm-clean")]
    public Task<IActionResult> ConfirmClean(Guid id, ConfirmTableCleanDto body, CancellationToken ct)
    {
        return Execute(async () => ManagementMapping.ToDto(await client.ConfirmCleanAsync(new IdRequest
        {
            Id = id.ToString(),
            Version = body.Version ?? string.Empty
        }, Options(ct))), ct);
    }

    [HttpGet("service")]
    public Task<IActionResult> GetServiceTables([FromQuery] ListQuery query, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var page = await client.GetServiceTablesAsync(ManagementMapping.ToWire(query), Options(ct));
            return new PageResult<ServiceTableDto>
            {
                Items = page.Items.Select(table => new ServiceTableDto
                {
                    TableId = Guid.Parse(table.TableId),
                    TableCode = table.TableCode,
                    AreaId = Guid.Parse(table.AreaId),
                    AreaName = table.AreaName,
                    RestaurantId = Guid.Parse(table.RestaurantId),
                    CleaningStatus = table.CleaningStatus,
                    Version = table.Version,
                    OpenSessionIds = table.OpenSessionIds.Select(Guid.Parse).ToList()
                }).ToList(),
                TotalCount = page.TotalCount,
                Page = page.Page,
                PageSize = page.PageSize
            };
        }, ct);
    }

    [HttpGet]
    public Task<IActionResult> GetAll([FromQuery] ListQuery query, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var page = await client.GetAllAsync(ManagementMapping.ToWire(query), Options(ct));
            return new PageResult<TableDto>
            {
                Items = page.Items.Select(ManagementMapping.ToDto).ToList(),
                TotalCount = page.TotalCount,
                Page = page.Page,
                PageSize = page.PageSize
            };
        }, ct);
    }

    [HttpGet("{id:guid}")]
    public Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        return Execute(async () =>
        {
            return ManagementMapping.ToDto(await client.GetByIdAsync(new IdRequest
            {
                Id = id.ToString()
            }, Options(ct)));
        }, ct);
    }

    [HttpPost]
    public Task<IActionResult> Create(TableWriteDto body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var result = await client.CreateAsync(ToRequest(Guid.Empty, body), Options(ct));
            Response.Headers.Location = $"/api/tables/{result.TableId}";
            return ManagementMapping.ToDto(result);
        }, ct, 201);
    }

    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, TableWriteDto body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            return ManagementMapping.ToDto(await client.UpdateAsync(ToRequest(id, body), Options(ct)));
        }, ct);
    }

    [HttpPatch("{id:guid}/status")]
    public Task<IActionResult> SetStatus(Guid id, StatusDto body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            return ManagementMapping.ToDto(await client.SetStatusAsync(new StatusRequest
            {
                Id = id.ToString(),
                IsActive = body.IsActive,
                Version = body.Version ?? ""
            }, Options(ct)));
        }, ct);
    }

    [HttpDelete("{id:guid}")]
    public Task<IActionResult> Delete(Guid id, [FromQuery] string version, CancellationToken ct)
    {
        return Execute(async () =>
        {
            await client.DeleteAsync(new IdRequest
            {
                Id = id.ToString(),
                Version = version
            }, Options(ct));
            return null;
        }, ct);
    }

    [HttpPatch("{id:guid}/area")]
    public Task<IActionResult> AssignArea(Guid id, AssignAreaDto body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            return ManagementMapping.ToDto(await client.AssignAreaAsync(new AssignAreaRequest
            {
                Id = id.ToString(),
                AreaId = body.AreaId.ToString(),
                Version = body.Version ?? ""
            }, Options(ct)));
        }, ct);
    }

    private static TableInfo ToRequest(Guid id, TableWriteDto body)
    {
        return ManagementMapping.ToWire(new TableDto
        {
            TableId = id,
            AreaId = body.AreaId,
            TableCode = body.TableCode,
            Capacity = body.Capacity,
            PositionX = body.PositionX,
            PositionY = body.PositionY,
            Version = body.Version
        });
    }
}
