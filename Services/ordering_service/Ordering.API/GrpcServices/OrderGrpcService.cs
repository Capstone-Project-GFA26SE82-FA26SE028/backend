using Backend.Contracts.Ordering;
using Grpc.Core;
using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;

namespace Ordering.API.GrpcServices;

public sealed class OrderGrpcService(IOrderService service) : OrderGrpc.OrderGrpcBase
{
    public override async Task<CreateOrderReply> Create(CreateOrderRequest request, ServerCallContext context)
    {
        var access = request.Access ?? new OrderingAccess();
        var (order, created) = await service.Create(
            Guid.Parse(access.TableSessionId),
            request.IdempotencyKey,
            new CreateOrderDTO
            {
                CartId = request.HasCartId ? Guid.Parse(request.CartId) : null,
                CartVersion = request.HasCartVersion ? request.CartVersion : null,
                TableId = request.HasTableId ? Guid.Parse(request.TableId) : null,
                Items = request.Items.Select(OrderingMapping.ReadSelection).ToList()
            },
            access.GuestToken,
            context.CancellationToken);

        return new CreateOrderReply
        {
            Result = OrderingMapping.Map(order),
            Created = created
        };
    }

    public override async Task<OrderInfo> GetById(OrderIdRequest request, ServerCallContext context)
    {
        var access = request.Access ?? new OrderingAccess();
        return OrderingMapping.Map(await service.GetById(
            Guid.Parse(access.TableSessionId),
            Guid.Parse(request.OrderId),
            access.GuestToken,
            context.CancellationToken));
    }

    public override async Task<OrderListReply> GetAll(OrderListRequest request, ServerCallContext context)
    {
        var access = request.Access ?? new OrderingAccess();
        var orders = await service.GetAll(
            Guid.Parse(access.TableSessionId),
            request.Page,
            request.PageSize,
            access.GuestToken,
            context.CancellationToken);

        var reply = new OrderListReply();
        reply.Items.AddRange(orders.Select(OrderingMapping.Map));
        return reply;
    }

    public override async Task<OrderInfo> Confirm(OrderIdRequest request, ServerCallContext context)
    {
        var access = request.Access ?? new OrderingAccess();
        return OrderingMapping.Map(await service.Confirm(
            Guid.Parse(access.TableSessionId),
            Guid.Parse(request.OrderId),
            request.Version,
            context.CancellationToken));
    }

    public override async Task<OrderInfo> ServeItem(ServeOrderItemRequest request, ServerCallContext context)
    {
        var access = request.Access ?? new OrderingAccess();
        return OrderingMapping.Map(await service.ServeItem(
            Guid.Parse(access.TableSessionId),
            Guid.Parse(request.OrderId),
            Guid.Parse(request.OrderItemId),
            request.HasVersion ? request.Version : null,
            context.CancellationToken));
    }
}
