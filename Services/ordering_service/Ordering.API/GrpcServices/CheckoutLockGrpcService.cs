using Backend.Contracts.Ordering;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;

namespace Ordering.API.GrpcServices;

public sealed class CheckoutLockGrpcService(ICheckoutLockService service) : CheckoutLockGrpc.CheckoutLockGrpcBase
{
    public override async Task<Empty> Verify(CheckoutLockRequest request, ServerCallContext context)
    {
        await service.Verify(Id(request.TableSessionId), request.GuestToken, context.CancellationToken);
        return new Empty();
    }

    public override async Task<CheckoutLockInfo> Begin(CheckoutLockRequest request, ServerCallContext context)
    {
        return Map(await service.Begin(Id(request.TableSessionId), Id(request.CheckoutId), request.GuestToken, context.CancellationToken));
    }

    public override async Task<CheckoutLockInfo> Release(CheckoutLockRequest request, ServerCallContext context)
    {
        return Map(await service.Release(Id(request.TableSessionId), Id(request.CheckoutId), context.CancellationToken));
    }

    public override async Task<CheckoutLockInfo> Bind(CheckoutLockRequest request, ServerCallContext context)
    {
        return Map(await service.Bind(Id(request.TableSessionId), Id(request.CheckoutId), Id(request.BillId), request.SourceHash, context.CancellationToken));
    }

    private static Guid Id(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "A valid ID is required."));
        }

        return id;
    }

    private static CheckoutLockInfo Map(CheckoutLockDTO value)
    {
        var result = new CheckoutLockInfo
        {
            CheckoutId = value.CheckoutId.ToString(),
            TableSessionId = value.TableSessionId.ToString(),
            Status = value.Status
        };
        if (value.SourceHash is not null)
        {
            result.SourceHash = value.SourceHash;
        }

        return result;
    }
}
