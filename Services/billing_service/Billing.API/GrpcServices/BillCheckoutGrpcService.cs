using System.Globalization;
using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Billing.API.GrpcServices;

public sealed class BillCheckoutGrpcService(IBillCheckoutService service) : BillCheckoutGrpc.BillCheckoutGrpcBase
{
    public override async Task<BillCheckoutInfo> Request(BillCheckoutRequest request, ServerCallContext context)
    {
        return Map(await service.Request(Id(request.TableSessionId), Id(request.CheckoutId), request.GuestToken, context.CancellationToken));
    }

    public override async Task<BillCheckoutInfo> GetCurrent(BillCheckoutRequest request, ServerCallContext context)
    {
        return Map(await service.GetCurrent(Id(request.TableSessionId), request.GuestToken, context.CancellationToken));
    }

    public override async Task<BillCheckoutInfo> Synchronize(BillCheckoutRequest request, ServerCallContext context)
    {
        return Map(await service.Synchronize(Id(request.TableSessionId), Id(request.CheckoutId), context.CancellationToken));
    }

    public override async Task<PreparedBillInfo> Prepare(BillCheckoutRequest request, ServerCallContext context)
    {
        return MapPrepared(await service.Prepare(Id(request.TableSessionId), Id(request.CheckoutId), context.CancellationToken));
    }

    public override async Task<PreparedBillInfo> PrepareAtCounter(BillCheckoutRequest request, ServerCallContext context)
    {
        return MapPrepared(await service.PrepareAtCounter(Id(request.TableSessionId), Id(request.CheckoutId), context.CancellationToken));
    }

    public override async Task<PreparedBillInfo> PresentAtCounter(BillCheckoutRequest request, ServerCallContext context)
    {
        return MapPrepared(await service.PresentAtCounter(Id(request.TableSessionId), Id(request.CheckoutId), context.CancellationToken));
    }

    public override async Task<PreparedBillInfo> GetPrepared(BillCheckoutRequest request, ServerCallContext context)
    {
        return MapPrepared(await service.GetPrepared(Id(request.TableSessionId), Id(request.CheckoutId), context.CancellationToken));
    }

    public override async Task<PreparedBillInfo> Present(BillCheckoutRequest request, ServerCallContext context)
    {
        return MapPrepared(await service.Present(Id(request.TableSessionId), Id(request.CheckoutId), context.CancellationToken));
    }

    public override async Task<BillCheckoutInfo> Cancel(BillCheckoutRequest request, ServerCallContext context)
    {
        return Map(await service.Cancel(Id(request.TableSessionId), Id(request.CheckoutId), context.CancellationToken));
    }

    public override async Task<Empty> AuthorizeRelease(BillCheckoutRequest request, ServerCallContext context)
    {
        await service.AuthorizeRelease(Id(request.TableSessionId), Id(request.CheckoutId), context.CancellationToken);
        return new Empty();
    }

    private static Guid Id(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Yêu cầu ID hợp lệ."));
        }

        return id;
    }

    private static PreparedBillInfo MapPrepared(PreparedBillDTO value)
    {
        var reply = new PreparedBillInfo
        {
            CheckoutId = value.CheckoutId.ToString(),
            TableSessionId = value.TableSessionId.ToString(),
            CheckoutStatus = value.CheckoutStatus,
            Bill = BillGrpcService.Map(value.Bill, includeCost: false),
            PreparedByRef = value.PreparedByRef.ToString(),
            PreparedAt = value.PreparedAt.ToString("O", CultureInfo.InvariantCulture)
        };
        if (value.PresentedAt.HasValue)
        {
            reply.PresentedAt = value.PresentedAt.Value.ToString("O", CultureInfo.InvariantCulture);
        }

        return reply;
    }

    private static BillCheckoutInfo Map(BillCheckoutDTO value)
    {
        var reply = new BillCheckoutInfo
        {
            CheckoutId = value.CheckoutId.ToString(),
            TableSessionId = value.TableSessionId.ToString(),
            Status = value.Status,
            OrderingReopened = value.OrderingReopened
        };
        if (value.BillId.HasValue)
        {
            reply.BillId = value.BillId.Value.ToString();
        }

        if (value.SourceHash is not null)
        {
            reply.SourceHash = value.SourceHash;
        }

        return reply;
    }
}
