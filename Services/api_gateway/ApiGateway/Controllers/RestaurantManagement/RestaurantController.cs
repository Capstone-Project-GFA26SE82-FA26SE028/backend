using ApiGateway.Swagger;
using Backend.Contracts.Restaurant;
using Microsoft.AspNetCore.Mvc;
using ApiGateway.DTOs.Restaurant;
using ApiGateway.Exceptions;

namespace ApiGateway.Controllers.RestaurantManagement;

[ApiController]
[Route("api/restaurants")]
[RequireBearerToken]
public sealed class RestaurantController(RestaurantGrpc.RestaurantGrpcClient client) : ManagementControllerBase
{
    [HttpGet]
    public Task<IActionResult> GetAll([FromQuery] ListQuery query, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var page = await client.GetAllAsync(ManagementMapping.ToWire(query), Options(ct));
            return new PageResult<RestaurantDto>
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
    public Task<IActionResult> Create(RestaurantWriteDto body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var result = await client.CreateAsync(ToRequest(Guid.Empty, body), Options(ct));
            Response.Headers.Location = $"/api/restaurants/{result.RestaurantId}";
            return ManagementMapping.ToDto(result);
        }, ct, 201);
    }

    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, RestaurantWriteDto body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            return ManagementMapping.ToDto(await client.UpdateAsync(ToRequest(id, body), Options(ct)));
        }, ct);
    }

    private static RestaurantInfo ToRequest(Guid id, RestaurantWriteDto body)
    {
        if (body.OpeningHours is null || body.OpeningHours.Any(x => x is null))
        {
            throw new GatewayRequestException(400, "Invalid operating hours list.");
        }

        return ManagementMapping.ToWire(new RestaurantDto
        {
            RestaurantId = id,
            RestaurantName = body.RestaurantName,
            Address = body.Address,
            Hotline = body.Hotline,
            TaxCode = body.TaxCode,
            LogoUrl = body.LogoUrl,
            TimeZone = body.TimeZone,
            OpeningHours = body.OpeningHours
        });
    }
}
