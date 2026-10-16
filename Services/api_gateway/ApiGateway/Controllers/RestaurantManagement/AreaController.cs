using ApiGateway.Swagger;
using Backend.Contracts.Restaurant;
using Microsoft.AspNetCore.Mvc;
using ApiGateway.DTOs.Restaurant;

namespace ApiGateway.Controllers.RestaurantManagement;

[ApiController]
[Route("api/areas")]
[RequireBearerToken]
public sealed class AreaController(AreaGrpc.AreaGrpcClient client) : ManagementControllerBase
{
    [HttpGet]
    public Task<IActionResult> GetAll([FromQuery] ListQuery query, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var page = await client.GetAllAsync(ManagementMapping.ToWire(query), Options(ct));
            return new PageResult<AreaDto>
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
    public Task<IActionResult> Create(AreaWriteDto body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var result = await client.CreateAsync(ToRequest(Guid.Empty, body), Options(ct));
            Response.Headers.Location = $"/api/areas/{result.AreaId}";
            return ManagementMapping.ToDto(result);
        }, ct, 201);
    }

    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, AreaWriteDto body, CancellationToken ct)
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
    public Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        return Execute(async () =>
        {
            await client.DeleteAsync(new IdRequest
            {
                Id = id.ToString()
            }, Options(ct));
            return null;
        }, ct);
    }

    private static AreaInfo ToRequest(Guid id, AreaWriteDto body)
    {
        return ManagementMapping.ToWire(new AreaDto
        {
            AreaId = id,
            RestaurantId = body.RestaurantId,
            AreaCode = body.AreaCode,
            AreaName = body.AreaName,
            ServiceType = body.ServiceType,
            SpaceType = body.SpaceType
        });
    }
}
