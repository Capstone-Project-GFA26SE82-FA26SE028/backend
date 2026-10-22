using Backend.Contracts.Ordering;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;

namespace Ordering.API.GrpcServices;

public sealed class CartGrpcService(ICartService service) : CartGrpc.CartGrpcBase
{
    public override async Task<CartInfo> Create(CartCreateRequest request, ServerCallContext context)
    {
        var access = request.Access ?? new OrderingAccess();
        return OrderingMapping.Map(await service.Create(Guid.Parse(access.TableSessionId), access.GuestToken, context.CancellationToken));
    }

    public override async Task<CartInfo> GetById(CartIdRequest request, ServerCallContext context)
    {
        var access = request.Access ?? new OrderingAccess();
        return OrderingMapping.Map(await service.GetById(Guid.Parse(access.TableSessionId), Guid.Parse(request.CartId),
            access.GuestToken, context.CancellationToken));
    }

    public override async Task<CartInfo> Update(CartUpdateRequest request, ServerCallContext context)
    {
        var access = request.Access ?? new OrderingAccess();
        return OrderingMapping.Map(await service.Update(Guid.Parse(access.TableSessionId), Guid.Parse(request.CartId),
            new CartWriteDTO
            {
                Version = request.Version,
                Items = request.Items.Select(OrderingMapping.ReadSelection).ToList()
            }, access.GuestToken, context.CancellationToken));
    }

    public override async Task<Empty> Delete(CartIdRequest request, ServerCallContext context)
    {
        var access = request.Access ?? new OrderingAccess();
        await service.Delete(Guid.Parse(access.TableSessionId), Guid.Parse(request.CartId), request.Version,
            access.GuestToken, context.CancellationToken);
        return new Empty();
    }
}
