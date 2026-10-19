using System.Globalization;
using Backend.Contracts.Catalog;
using Catalog.BLL.Interfaces;
using Catalog.Common.DTOs;
using Grpc.Core;
using Shared.Common.DTOs;
using Shared.Common.Pagination;

namespace Catalog.API.GrpcServices;

public sealed class OptionGrpcService(IOptionService service) : OptionGrpc.OptionGrpcBase
{
    public override async Task<OptionReply> Create(CreateOptionRequest request, ServerCallContext context)
    {
        return ToReply(await service.Create(new CreateOptionDTO
        {
            OptionGroupId = ParseId(request.OptionGroupId),
            OptionName = request.OptionName,
            ExtraPrice = request.ExtraPrice
        }, context.CancellationToken));
    }

    public override async Task<OptionListReply> GetAll(OptionListRequest request, ServerCallContext context)
    {
        var response = await service.GetAll(new OptionQueryDTO
        {
            Page = request.Page,
            PageSize = request.PageSize,
            Search = request.HasSearch ? request.Search : null,
            OptionGroupId = request.HasOptionGroupId ? ParseId(request.OptionGroupId) : null,
            Availability = request.HasAvailability ? request.Availability : null
        }, context.CancellationToken);
        var reply = new OptionListReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? string.Empty,
            IsSuccess = response.IsSuccess
        };
        if (response.Result is PagedResult<OptionDTO> page)
        {
            reply.Items.AddRange(page.Items.Select(Map));
            reply.TotalCount = page.TotalCount;
            reply.Page = page.Page;
            reply.PageSize = page.PageSize;
        }

        return reply;
    }

    public override async Task<OptionReply> GetById(OptionIdRequest request, ServerCallContext context)
    {
        return ToReply(await service.GetById(ParseId(request.OptionId), context.CancellationToken));
    }

    public override async Task<OptionReply> Update(UpdateOptionRequest request, ServerCallContext context)
    {
        return ToReply(await service.Update(ParseId(request.OptionId), new UpdateOptionDTO
        {
            OptionName = request.OptionName,
            ExtraPrice = request.ExtraPrice
        }, context.CancellationToken));
    }

    public override async Task<OptionReply> Delete(OptionIdRequest request, ServerCallContext context)
    {
        return ToReply(await service.Delete(ParseId(request.OptionId), context.CancellationToken));
    }

    public override async Task<OptionReply> SetStatus(SetOptionStatusRequest request, ServerCallContext context)
    {
        return ToReply(await service.SetStatus(ParseId(request.OptionId), new SetOptionStatusDTO
        {
            Availability = request.Availability
        }, context.CancellationToken));
    }

    private static Guid ParseId(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid resource ID."));
        }

        return id;
    }

    private static OptionReply ToReply(ResponseDTO response)
    {
        return new OptionReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? string.Empty,
            IsSuccess = response.IsSuccess,
            Result = response.Result is OptionDTO entity ? Map(entity) : null
        };
    }

    private static OptionInfo Map(OptionDTO entity)
    {
        return new OptionInfo
        {
            OptionId = entity.OptionId.ToString(),
            OptionGroupId = entity.OptionGroupId.ToString(),
            OptionName = entity.OptionName,
            ExtraPrice = entity.ExtraPrice.ToString(CultureInfo.InvariantCulture),
            Availability = entity.Availability
        };
    }
}
