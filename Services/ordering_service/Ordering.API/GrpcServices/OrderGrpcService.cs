using Backend.Contracts.Ordering;
using Grpc.Core;
using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;

namespace Ordering.API.GrpcServices;

public sealed class OrderGrpcService(IOrderService service) : OrderGrpc.OrderGrpcBase
{
    public override async Task<RefundAccountInfo> GetRefundAccount(TakeawayIdRequest request, ServerCallContext context)
    {
        return MapRefund(await service.GetRefundAccount(Guid.Parse(request.RestaurantId), Guid.Parse(request.OrderId), context.CancellationToken));
    }

    public override async Task<RefundAccountInfo> SaveRefundAccount(RefundAccountRequest request, ServerCallContext context)
    {
        return MapRefund(await service.SaveRefundAccount(Guid.Parse(request.RestaurantId), Guid.Parse(request.OrderId), new RefundAccountDTO
        {
            BankName = request.BankName,
            AccountNumber = request.AccountNumber,
            AccountHolder = request.AccountHolder
        }, context.CancellationToken));
    }

    private static RefundAccountInfo MapRefund(RefundAccountDTO value)
    {
        return new RefundAccountInfo
        {
            Submitted = value.Submitted,
            BankName = value.BankName,
            AccountNumber = value.AccountNumber,
            AccountHolder = value.AccountHolder
        };
    }

    public override async Task<CreateOrderReply> CreateTakeaway(TakeawayCreateRequest request, ServerCallContext context)
    {
        if (request.Items.Count > 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Vui lòng gửi giỏ hàng của khách hàng thay vì danh sách món do nhân viên nhập."));
        }

        var (order, created) = await service.CreateTakeaway(Guid.Parse(request.RestaurantId),
            request.IdempotencyKey, new SubmitTakeawayDTO
            {
                CartId = Guid.Parse(request.CartId),
                CartVersion = request.CartVersion,
                PickupName = request.PickupName,
                PickupPhone = request.PickupPhone,
            }, context.CancellationToken);
        return new CreateOrderReply
        {
            Result = OrderingMapping.Map(order),
            Created = created
        };
    }

    public override async Task<OrderInfo> GetTakeawayById(TakeawayIdRequest request, ServerCallContext context)
    {
        return OrderingMapping.Map(await service.GetTakeawayById(Guid.Parse(request.RestaurantId),
            Guid.Parse(request.OrderId), context.CancellationToken));
    }

    public override async Task<OrderListReply> GetAllTakeaway(TakeawayListRequest request, ServerCallContext context)
    {
        var orders = await service.GetAllTakeaway(Guid.Parse(request.RestaurantId),
            request.HasStatus ? request.Status : null, request.Page, request.PageSize, context.CancellationToken);
        var reply = new OrderListReply();
        reply.Items.AddRange(orders.Select(OrderingMapping.Map));
        return reply;
    }

    public override async Task<OrderInfo> CollectTakeaway(TakeawayIdRequest request, ServerCallContext context)
    {
        return OrderingMapping.Map(await service.CollectTakeaway(Guid.Parse(request.RestaurantId),
            Guid.Parse(request.OrderId), request.Version, request.PickupCode, request.HandoverConfirmed, context.CancellationToken));
    }

    public override async Task<OrderInfo> CancelTakeaway(TakeawayIdRequest request, ServerCallContext context)
    {
        return OrderingMapping.Map(await service.CancelTakeaway(Guid.Parse(request.RestaurantId),
            Guid.Parse(request.OrderId), request.Version, context.CancellationToken));
    }

    public override async Task<OrderInfo> AcceptTakeaway(TakeawayIdRequest request, ServerCallContext context)
    {
        return OrderingMapping.Map(await service.AcceptTakeaway(Guid.Parse(request.RestaurantId),
            Guid.Parse(request.OrderId), request.Version, context.CancellationToken));
    }

    public override async Task<OrderInfo> RejectTakeaway(TakeawayIdRequest request, ServerCallContext context)
    {
        return OrderingMapping.Map(await service.RejectTakeaway(Guid.Parse(request.RestaurantId),
            Guid.Parse(request.OrderId), request.Version, request.Reason, context.CancellationToken));
    }

    public override async Task<OrderInfo> VerifyTakeawayRefund(TakeawayIdRequest request, ServerCallContext context)
    {
        return OrderingMapping.Map(await service.VerifyTakeawayRefund(Guid.Parse(request.RestaurantId),
            Guid.Parse(request.OrderId), request.RefundReference, request.RefundEvidence, context.CancellationToken));
    }

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
                Channel = request.HasChannel ? request.Channel : null,
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
