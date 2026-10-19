using System.Globalization;
using Backend.Contracts.Catalog;
using Catalog.BLL.Interfaces;
using Catalog.Common.DTOs;
using Grpc.Core;
using Shared.Common.DTOs;
using Shared.Common.Pagination;

namespace Catalog.API.GrpcServices;

public sealed class PriceGrpcService(IPriceService service) : PriceGrpc.PriceGrpcBase
{
    public override async Task<PriceReply> Create(CreatePriceRequest request, ServerCallContext context)
    {
        return ToReply(await service.Create(new CreatePriceDTO
        {
            ItemId = ParseId(request.ItemId),
            Channel = request.Channel,
            Amount = request.Amount,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.HasEffectiveTo ? request.EffectiveTo : null,
            DailyStart = request.HasDailyStart ? request.DailyStart : null,
            DailyEnd = request.HasDailyEnd ? request.DailyEnd : null
        }, context.CancellationToken));
    }

    public override async Task<PriceReply> GetById(PriceIdRequest request, ServerCallContext context)
    {
        return ToReply(await service.GetById(ParseId(request.PriceId), context.CancellationToken));
    }

    public override async Task<PriceListReply> GetAll(PriceListRequest request, ServerCallContext context)
    {
        var response = await service.GetAll(new PriceQueryDTO
        {
            ItemId = request.HasItemId ? ParseId(request.ItemId) : null,
            Channel = request.HasChannel ? request.Channel : null,
            Page = request.Page,
            PageSize = request.PageSize
        }, context.CancellationToken);
        var reply = new PriceListReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? string.Empty,
            IsSuccess = response.IsSuccess
        };
        if (response.Result is PagedResult<PriceDTO> page)
        {
            reply.Items.AddRange(page.Items.Select(Map));
            reply.TotalCount = page.TotalCount;
            reply.Page = page.Page;
            reply.PageSize = page.PageSize;
        }

        return reply;
    }

    public override async Task<PriceReply> Update(UpdatePriceRequest request, ServerCallContext context)
    {
        return ToReply(await service.Update(ParseId(request.PriceId), new UpdatePriceDTO
        {
            Channel = request.Channel,
            Amount = request.Amount,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.HasEffectiveTo ? request.EffectiveTo : null,
            DailyStart = request.HasDailyStart ? request.DailyStart : null,
            DailyEnd = request.HasDailyEnd ? request.DailyEnd : null
        }, context.CancellationToken));
    }

    public override async Task<PriceReply> Delete(DeletePriceRequest request, ServerCallContext context)
    {
        return ToReply(await service.Delete(ParseId(request.PriceId), context.CancellationToken));
    }

    public override async Task<PriceReply> Replace(ReplacePriceRequest request, ServerCallContext context)
    {
        return ToReply(await service.Replace(ParseId(request.PriceId), new ReplacePriceDTO
        {
            Amount = request.Amount,
            EffectiveTo = request.HasEffectiveTo ? request.EffectiveTo : null
        }, context.CancellationToken));
    }

    public override async Task<AppliedPriceReply> GetAppliedPrice(AppliedPriceRequest request, ServerCallContext context)
    {
        var response = await service.GetAppliedPrice(ParseId(request.ItemId), request.Channel,
            request.HasAt ? request.At : null, context.CancellationToken);
        var reply = new AppliedPriceReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? string.Empty,
            IsSuccess = response.IsSuccess
        };
        if (response.Result is AppliedPriceDTO applied)
        {
            reply.Result = new AppliedPriceInfo
            {
                ItemId = applied.ItemId.ToString(),
                PriceId = applied.PriceId.ToString(),
                Amount = applied.Amount.ToString(CultureInfo.InvariantCulture),
                RequestedChannel = applied.RequestedChannel,
                PriceChannel = applied.PriceChannel,
                EvaluatedAt = applied.EvaluatedAt.ToString("O", CultureInfo.InvariantCulture),
                HasValidStandardPrice = applied.HasValidStandardPrice,
                Price = Map(applied.Price)
            };
        }

        return reply;
    }

    private static Guid ParseId(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid resource ID."));
        }

        return id;
    }

    private static PriceReply ToReply(ResponseDTO response)
    {
        return new PriceReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? string.Empty,
            IsSuccess = response.IsSuccess,
            Result = response.Result is PriceDTO price ? Map(price) : null
        };
    }

    private static PriceInfo Map(PriceDTO price)
    {
        var result = new PriceInfo
        {
            PriceId = price.PriceId.ToString(),
            ItemId = price.ItemId.ToString(),
            Channel = price.Channel,
            Amount = price.Amount.ToString(CultureInfo.InvariantCulture),
            EffectiveFrom = price.EffectiveFrom.ToString("O", CultureInfo.InvariantCulture)
        };
        if (price.EffectiveTo.HasValue)
        {
            result.EffectiveTo = price.EffectiveTo.Value.ToString("O", CultureInfo.InvariantCulture);
        }

        if (price.DailyStart.HasValue)
        {
            result.DailyStart = price.DailyStart.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }

        if (price.DailyEnd.HasValue)
        {
            result.DailyEnd = price.DailyEnd.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return result;
    }
}
