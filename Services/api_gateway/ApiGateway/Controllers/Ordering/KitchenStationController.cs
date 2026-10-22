using ApiGateway.DTOs.Ordering;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Ordering;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ApiGateway.Controllers.Ordering;

[ApiController]
[Route("api/kitchen-stations")]
[RequireBearerToken]
public sealed class KitchenStationController(
    KitchenStationGrpc.KitchenStationGrpcClient client, IOptions<OrderingClientSettings> settings)
    : OrderingControllerBase(settings)
{
    [HttpPost]
    public Task<IActionResult> Create(KitchenStationWriteDTO body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.CreateAsync(Write(body), Options(ct));
            return Result(Map(reply), 201);
        }, ct);
    }

    [HttpGet]
    public Task<IActionResult> GetAll(CancellationToken ct, int page = 1, int pageSize = 20,
        string? search = null, bool? isActive = null)
    {
        return Execute(async () =>
        {
            var request = new KitchenStationListRequest
            {
                Page = page,
                PageSize = pageSize
            };
            if (search is not null)
            {
                request.Search = search;
            }
            if (isActive.HasValue)
            {
                request.IsActive = isActive.Value;
            }
            var reply = await client.GetAllAsync(request, Options(ct));
            return Result(new
            {
                Items = reply.Items.Select(Map).ToList(),
                reply.TotalCount,
                reply.Page,
                reply.PageSize
            });
        }, ct);
    }

    [HttpGet("{id:guid}")]
    public Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.GetByIdAsync(new KitchenStationIdRequest
            {
                Id = id.ToString()
            }, Options(ct));
            return Result(Map(reply));
        }, ct);
    }

    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, KitchenStationWriteDTO body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var request = Write(body);
            request.Id = id.ToString();
            return Result(Map(await client.UpdateAsync(request, Options(ct))));
        }, ct);
    }

    [HttpPatch("{id:guid}/status")]
    public Task<IActionResult> SetStatus(Guid id, KitchenStationStatusDTO body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            return Result(Map(await client.SetStatusAsync(new KitchenStationStatusRequest
            {
                Id = id.ToString(),
                IsActive = body.IsActive!.Value
            }, Options(ct))));
        }, ct);
    }

    private static KitchenStationWriteRequest Write(KitchenStationWriteDTO body)
    {
        return new KitchenStationWriteRequest
        {
            Code = body.KitchenStationCode,
            Name = body.KitchenStationName
        };
    }

    private static KitchenStationDTO Map(KitchenStationInfo station)
    {
        return new KitchenStationDTO
        {
            KitchenStationId = Guid.Parse(station.Id),
            KitchenStationCode = station.Code,
            KitchenStationName = station.Name,
            IsActive = station.IsActive
        };
    }
}
