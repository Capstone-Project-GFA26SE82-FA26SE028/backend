using Backend.Contracts.Catalog;
using Catalog.BLL.Interfaces;
using Catalog.Common.DTOs;
using Grpc.Core;
using Shared.Common.DTOs;
using Shared.Common.Pagination;

namespace Catalog.API.GrpcServices;

public sealed class ItemOptionGroupGrpcService(IItemOptionGroupService service) : ItemOptionGroupGrpc.ItemOptionGroupGrpcBase
{
    public override async Task<ItemOptionGroupReply> Create(CreateItemOptionGroupRequest request, ServerCallContext context)
    {
        return ToReply(await service.Create(ParseId(request.ItemId), new CreateItemOptionGroupDTO
        {
            OptionGroupId = ParseId(request.OptionGroupId),
            MinSelection = request.HasMinSelection ? request.MinSelection : null,
            MaxSelection = request.HasMaxSelection ? request.MaxSelection : null
        }, context.CancellationToken));
    }

    public override async Task<ItemOptionGroupListReply> GetAll(ItemOptionGroupListRequest request, ServerCallContext context)
    {
        var response = await service.GetAll(ParseId(request.ItemId), context.CancellationToken);
        var reply = new ItemOptionGroupListReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? string.Empty,
            IsSuccess = response.IsSuccess
        };
        if (response.Result is ItemOptionGroupDTO[] items)
        {
            reply.Items.AddRange(items.Select(Map));
        }

        return reply;
    }

    public override async Task<ItemOptionGroupReply> GetById(ItemOptionGroupIdRequest request, ServerCallContext context)
    {
        return ToReply(await service.GetById(ParseId(request.ItemId), ParseId(request.OptionGroupId), context.CancellationToken));
    }

    public override async Task<ItemOptionGroupReply> Update(UpdateItemOptionGroupRequest request, ServerCallContext context)
    {
        return ToReply(await service.Update(ParseId(request.ItemId), ParseId(request.OptionGroupId), new UpdateItemOptionGroupDTO
        {
            MinSelection = request.HasMinSelection ? request.MinSelection : null,
            MaxSelection = request.HasMaxSelection ? request.MaxSelection : null
        }, context.CancellationToken));
    }

    public override async Task<ItemOptionGroupReply> Delete(ItemOptionGroupIdRequest request, ServerCallContext context)
    {
        return ToReply(await service.Delete(ParseId(request.ItemId), ParseId(request.OptionGroupId), context.CancellationToken));
    }

    private static Guid ParseId(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "ID tài nguyên không hợp lệ."));
        }

        return id;
    }

    private static ItemOptionGroupReply ToReply(ResponseDTO response)
    {
        return new ItemOptionGroupReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? string.Empty,
            IsSuccess = response.IsSuccess,
            Result = response.Result is ItemOptionGroupDTO entity ? Map(entity) : null
        };
    }

    private static ItemOptionGroupInfo Map(ItemOptionGroupDTO entity)
    {
        return new ItemOptionGroupInfo
        {
            ItemId = entity.ItemId.ToString(),
            OptionGroupId = entity.OptionGroupId.ToString(),
            OptionGroupName = entity.OptionGroupName,
            MinSelection = entity.MinSelection,
            MaxSelection = entity.MaxSelection
        };
    }
}
