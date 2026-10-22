using ApiGateway.DTOs.Catalog;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Catalog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;
using Shared.Common.Pagination;

namespace ApiGateway.Controllers.CatalogController;

[ApiController]
[Route("api/items")]
[RequireBearerToken]
public sealed class ItemController(
    ItemGrpc.ItemGrpcClient client,
    IOptions<CatalogClientSettings> settings) : CatalogControllerBase(settings)
{
    [HttpPost]
    public Task<IActionResult> Create(CreateItemDTO body, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var request = new CreateItemRequest
            {
                CategoryId = body.CategoryId.ToString(),
                ItemName = body.ItemName ?? string.Empty
            };
            if (body.Description is not null)
            {
                request.Description = body.Description;
            }

            if (body.ImageUrl is not null)
            {
                request.ImageUrl = body.ImageUrl;
            }

            var reply = await client.CreateAsync(request, Options(cancellationToken));
            if (reply.IsSuccess && reply.StatusCode == 201 && reply.Result is not null)
            {
                Response.Headers.Location = $"/api/items/{Uri.EscapeDataString(reply.Result.ItemId)}";
            }

            return ToResponse(reply);
        }, cancellationToken);
    }

    [HttpGet]
    public Task<IActionResult> GetAll([FromQuery] ItemQueryDTO query, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var request = new ItemListRequest
            {
                Page = query.Page,
                PageSize = query.PageSize
            };
            if (query.CategoryId.HasValue)
            {
                request.CategoryId = query.CategoryId.Value.ToString();
            }

            if (query.Search is not null)
            {
                request.Search = query.Search;
            }

            if (query.Availability is not null)
            {
                request.Availability = query.Availability;
            }

            var reply = await client.GetAllAsync(request, Options(cancellationToken));
            return StatusCode(reply.StatusCode, new ResponseDTO
            {
                StatusCode = reply.StatusCode,
                Message = reply.Message,
                IsSuccess = reply.IsSuccess,
                Result = reply.IsSuccess
                    ? new PagedResult<ItemDTO>(reply.Items.Select(Map).ToArray(),
                        reply.TotalCount, reply.Page, reply.PageSize)
                    : null
            });
        }, cancellationToken);
    }

    [HttpGet("{id}")]
    public Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var reply = await client.GetByIdAsync(new ItemIdRequest
            {
                ItemId = id.ToString()
            }, Options(cancellationToken));
            return ToResponse(reply);
        }, cancellationToken);
    }

    [HttpPut("{id}")]
    public Task<IActionResult> Update(Guid id, UpdateItemDTO body, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var request = new UpdateItemRequest
            {
                ItemId = id.ToString(),
                CategoryId = body.CategoryId.ToString(),
                ItemName = body.ItemName ?? string.Empty,
                Version = body.Version ?? string.Empty
            };
            if (body.Description is not null)
            {
                request.Description = body.Description;
            }

            if (body.ImageUrl is not null)
            {
                request.ImageUrl = body.ImageUrl;
            }

            var reply = await client.UpdateAsync(request, Options(cancellationToken));
            return ToResponse(reply);
        }, cancellationToken);
    }

    [HttpPatch("{id}/kitchen-station")]
    public Task<IActionResult> SetKitchenStation(Guid id, SetItemKitchenStationDTO body, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var request = new SetItemKitchenStationRequest
            {
                ItemId = id.ToString(),
                Version = body.Version ?? string.Empty
            };
            if (body.KitchenStationId.HasValue)
            {
                request.KitchenStationId = body.KitchenStationId.Value.ToString();
            }
            return ToResponse(await client.SetKitchenStationAsync(request, Options(cancellationToken)));
        }, cancellationToken);
    }

    [HttpPatch("{id}/status")]
    public Task<IActionResult> SetStatus(Guid id, SetItemStatusDTO body, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var reply = await client.SetStatusAsync(new SetItemStatusRequest
            {
                ItemId = id.ToString(),
                Availability = body.Availability ?? string.Empty,
                Version = body.Version ?? string.Empty
            }, Options(cancellationToken));
            return ToResponse(reply);
        }, cancellationToken);
    }

    private IActionResult ToResponse(ItemReply reply)
    {
        return StatusCode(reply.StatusCode, new ResponseDTO
        {
            StatusCode = reply.StatusCode,
            Message = reply.Message,
            IsSuccess = reply.IsSuccess,
            Result = reply.Result is null ? null : Map(reply.Result)
        });
    }

    private static ItemDTO Map(ItemInfo item)
    {
        return new ItemDTO
        {
            ItemId = Guid.Parse(item.ItemId),
            CategoryId = Guid.Parse(item.CategoryId),
            CategoryName = item.CategoryName,
            ItemCode = item.ItemCode,
            ItemName = item.ItemName,
            Description = item.HasDescription ? item.Description : null,
            ImageUrl = item.HasImageUrl ? item.ImageUrl : null,
            Availability = item.Availability,
            KitchenStationId = item.HasKitchenStationId ? Guid.Parse(item.KitchenStationId) : null,
            Version = item.Version
        };
    }
}
