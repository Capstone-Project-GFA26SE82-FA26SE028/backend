using ApiGateway.DTOs.Catalog;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Catalog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.CatalogController;

[ApiController]
[Route("api/items/{itemId}/option-groups")]
[RequireBearerToken]
public sealed class ItemOptionGroupController(
    ItemOptionGroupGrpc.ItemOptionGroupGrpcClient client,
    IOptions<CatalogClientSettings> settings) : CatalogControllerBase(settings)
{
    [HttpPost]
    public Task<IActionResult> Create(Guid itemId, CreateItemOptionGroupDTO body, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var request = new CreateItemOptionGroupRequest
            {
                ItemId = itemId.ToString(),
                OptionGroupId = body.OptionGroupId.ToString()
            };
            if (body.MinSelection.HasValue)
            {
                request.MinSelection = body.MinSelection.Value;
            }

            if (body.MaxSelection.HasValue)
            {
                request.MaxSelection = body.MaxSelection.Value;
            }

            return ToResponse(await client.CreateAsync(request, Options(cancellationToken)));
        }, cancellationToken);
    }

    [HttpGet]
    public Task<IActionResult> GetAll(Guid itemId, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var reply = await client.GetAllAsync(new ItemOptionGroupListRequest
            {
                ItemId = itemId.ToString()
            }, Options(cancellationToken));
            return StatusCode(reply.StatusCode, new ResponseDTO
            {
                StatusCode = reply.StatusCode,
                Message = reply.Message,
                IsSuccess = reply.IsSuccess,
                Result = reply.IsSuccess ? reply.Items.Select(Map).ToArray() : null
            });
        }, cancellationToken);
    }

    [HttpGet("{groupId}")]
    public Task<IActionResult> GetById(Guid itemId, Guid groupId, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            return ToResponse(await client.GetByIdAsync(new ItemOptionGroupIdRequest
            {
                ItemId = itemId.ToString(),
                OptionGroupId = groupId.ToString()
            }, Options(cancellationToken)));
        }, cancellationToken);
    }

    [HttpPut("{groupId}")]
    public Task<IActionResult> Update(
        Guid itemId, Guid groupId, UpdateItemOptionGroupDTO body, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var request = new UpdateItemOptionGroupRequest
            {
                ItemId = itemId.ToString(),
                OptionGroupId = groupId.ToString()
            };
            if (body.MinSelection.HasValue)
            {
                request.MinSelection = body.MinSelection.Value;
            }

            if (body.MaxSelection.HasValue)
            {
                request.MaxSelection = body.MaxSelection.Value;
            }

            return ToResponse(await client.UpdateAsync(request, Options(cancellationToken)));
        }, cancellationToken);
    }

    [HttpDelete("{groupId}")]
    public Task<IActionResult> Delete(Guid itemId, Guid groupId, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            return ToResponse(await client.DeleteAsync(new ItemOptionGroupIdRequest
            {
                ItemId = itemId.ToString(),
                OptionGroupId = groupId.ToString()
            }, Options(cancellationToken)));
        }, cancellationToken);
    }

    private IActionResult ToResponse(ItemOptionGroupReply reply)
    {
        if (reply.IsSuccess && reply.StatusCode == 201 && reply.Result is not null)
        {
            Response.Headers.Location = $"/api/items/{Uri.EscapeDataString(reply.Result.ItemId)}/option-groups/{Uri.EscapeDataString(reply.Result.OptionGroupId)}";
        }

        return StatusCode(reply.StatusCode, new ResponseDTO
        {
            StatusCode = reply.StatusCode,
            Message = reply.Message,
            IsSuccess = reply.IsSuccess,
            Result = reply.Result is null ? null : Map(reply.Result)
        });
    }

    private static ItemOptionGroupDTO Map(ItemOptionGroupInfo entity)
    {
        return new ItemOptionGroupDTO
        {
            ItemId = Guid.Parse(entity.ItemId),
            OptionGroupId = Guid.Parse(entity.OptionGroupId),
            OptionGroupName = entity.OptionGroupName,
            MinSelection = entity.MinSelection,
            MaxSelection = entity.MaxSelection
        };
    }
}
