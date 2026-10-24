using Backend.Contracts.Catalog;
using Catalog.BLL.Interfaces;
using Catalog.Common.DTOs;
using Grpc.Core;
using Shared.Common.DTOs;
using Shared.Common.Pagination;

namespace Catalog.API.GrpcServices;

public sealed class ItemGrpcService(IItemService service) : ItemGrpc.ItemGrpcBase
{
    public override async Task<ItemReply> SetKitchenStation(SetItemKitchenStationRequest request, ServerCallContext context)
    {
        return ToReply(await service.SetKitchenStation(ParseId(request.ItemId, "item"),
            new SetItemKitchenStationDTO
            {
                KitchenStationId = request.HasKitchenStationId ? ParseId(request.KitchenStationId, "station") : null,
                Version = request.Version
            }, context.CancellationToken));
    }

    public override async Task<ItemReply> Create(CreateItemRequest request, ServerCallContext context)
    {
        var response = await service.Create(new CreateItemDTO
        {
            CategoryId = ParseId(request.CategoryId, "category"),
            ItemName = request.ItemName,
            Description = request.HasDescription ? request.Description : null,
            ImageUrl = request.HasImageUrl ? request.ImageUrl : null,
            PreparationTimeMinutes = request.HasPreparationTimeMinutes ? request.PreparationTimeMinutes : null
        }, context.CancellationToken);
        return ToReply(response);
    }

    public override async Task<ItemListReply> GetAll(ItemListRequest request, ServerCallContext context)
    {
        var response = await service.GetAll(new ItemQueryDTO
        {
            CategoryId = request.HasCategoryId ? ParseId(request.CategoryId, "category") : null,
            Search = request.HasSearch ? request.Search : null,
            Availability = request.HasAvailability ? request.Availability : null,
            Page = request.Page,
            PageSize = request.PageSize
        }, context.CancellationToken);

        var reply = new ItemListReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? string.Empty,
            IsSuccess = response.IsSuccess
        };
        if (response.Result is PagedResult<ItemDTO> page)
        {
            reply.Items.AddRange(page.Items.Select(Map));
            reply.TotalCount = page.TotalCount;
            reply.Page = page.Page;
            reply.PageSize = page.PageSize;
        }

        return reply;
    }

    public override async Task<ItemReply> GetById(ItemIdRequest request, ServerCallContext context)
    {
        return ToReply(await service.GetById(ParseId(request.ItemId, "item"), context.CancellationToken));
    }

    public override async Task<ItemReply> Update(UpdateItemRequest request, ServerCallContext context)
    {
        var response = await service.Update(ParseId(request.ItemId, "item"), new UpdateItemDTO
        {
            CategoryId = ParseId(request.CategoryId, "category"),
            ItemName = request.ItemName,
            Description = request.HasDescription ? request.Description : null,
            ImageUrl = request.HasImageUrl ? request.ImageUrl : null,
            PreparationTimeMinutes = request.HasPreparationTimeMinutes ? request.PreparationTimeMinutes : null,
            Version = request.Version
        }, context.CancellationToken);
        return ToReply(response);
    }

    public override async Task<ItemReply> SetStatus(SetItemStatusRequest request, ServerCallContext context)
    {
        var response = await service.SetStatus(ParseId(request.ItemId, "item"), new SetItemStatusDTO
        {
            Availability = request.Availability,
            Version = request.Version
        }, context.CancellationToken);
        return ToReply(response);
    }

    private static Guid ParseId(string value, string resource)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Invalid {resource} ID."));
        }

        return id;
    }

    private static ItemReply ToReply(ResponseDTO response)
    {
        return new ItemReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? string.Empty,
            IsSuccess = response.IsSuccess,
            Result = response.Result is ItemDTO item ? Map(item) : null
        };
    }

    private static ItemInfo Map(ItemDTO item)
    {
        var result = new ItemInfo
        {
            ItemId = item.ItemId.ToString(),
            CategoryId = item.CategoryId.ToString(),
            CategoryName = item.CategoryName,
            ItemCode = item.ItemCode,
            ItemName = item.ItemName,
            Availability = item.Availability,
            Version = item.Version
        };
        if (item.PreparationTimeMinutes.HasValue)
        {
            result.PreparationTimeMinutes = item.PreparationTimeMinutes.Value;
        }
        if (item.KitchenStationId.HasValue)
        {
            result.KitchenStationId = item.KitchenStationId.Value.ToString();
        }
        if (item.Description is not null)
        {
            result.Description = item.Description;
        }

        if (item.ImageUrl is not null)
        {
            result.ImageUrl = item.ImageUrl;
        }

        return result;
    }
}
