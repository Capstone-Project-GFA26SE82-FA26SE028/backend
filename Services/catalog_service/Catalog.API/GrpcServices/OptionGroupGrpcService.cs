using Backend.Contracts.Catalog;
using Catalog.BLL.Interfaces;
using Catalog.Common.DTOs;
using Grpc.Core;
using Shared.Common.DTOs;
using Shared.Common.Pagination;

namespace Catalog.API.GrpcServices;

public sealed class OptionGroupGrpcService(IOptionGroupService service) : OptionGroupGrpc.OptionGroupGrpcBase
{
    public override async Task<OptionGroupReply> Create(CreateOptionGroupRequest request, ServerCallContext context)
    {
        return ToReply(await service.Create(new CreateOptionGroupDTO
        {
            OptionGroupName = request.OptionGroupName
        }, context.CancellationToken));
    }

    public override async Task<OptionGroupListReply> GetAll(OptionGroupListRequest request, ServerCallContext context)
    {
        var response = await service.GetAll(new OptionGroupQueryDTO
        {
            Page = request.Page,
            PageSize = request.PageSize,
            Search = request.HasSearch ? request.Search : null
        }, context.CancellationToken);
        var reply = new OptionGroupListReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? string.Empty,
            IsSuccess = response.IsSuccess
        };
        if (response.Result is PagedResult<OptionGroupDTO> page)
        {
            reply.Items.AddRange(page.Items.Select(Map));
            reply.TotalCount = page.TotalCount;
            reply.Page = page.Page;
            reply.PageSize = page.PageSize;
        }

        return reply;
    }

    public override async Task<OptionGroupReply> GetById(OptionGroupIdRequest request, ServerCallContext context)
    {
        return ToReply(await service.GetById(ParseId(request.OptionGroupId), context.CancellationToken));
    }

    public override async Task<OptionGroupReply> Update(UpdateOptionGroupRequest request, ServerCallContext context)
    {
        return ToReply(await service.Update(ParseId(request.OptionGroupId), new UpdateOptionGroupDTO
        {
            OptionGroupName = request.OptionGroupName
        }, context.CancellationToken));
    }

    public override async Task<OptionGroupReply> Delete(OptionGroupIdRequest request, ServerCallContext context)
    {
        return ToReply(await service.Delete(ParseId(request.OptionGroupId), context.CancellationToken));
    }

    private static Guid ParseId(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid resource ID."));
        }

        return id;
    }

    private static OptionGroupReply ToReply(ResponseDTO response)
    {
        return new OptionGroupReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? string.Empty,
            IsSuccess = response.IsSuccess,
            Result = response.Result is OptionGroupDTO entity ? Map(entity) : null
        };
    }

    private static OptionGroupInfo Map(OptionGroupDTO entity)
    {
        return new OptionGroupInfo
        {
            OptionGroupId = entity.OptionGroupId.ToString(),
            OptionGroupName = entity.OptionGroupName
        };
    }
}
