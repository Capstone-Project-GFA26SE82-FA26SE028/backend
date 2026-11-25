using System.Globalization;
using Backend.Contracts.Ordering;
using Grpc.Core;
using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;
using Shared.Persistence.RestaurantIsolation;

namespace Ordering.API.GrpcServices;

public sealed class BillingOrderGrpcService(IBillingOrderService service, IOrderingAccess access, RestaurantDataScope scope)
    : BillingOrderGrpc.BillingOrderGrpcBase
{
    public override async Task<BillingOrderSnapshot> GetBySession(BillingOrderRequest request, ServerCallContext context)
    {
        return Map(await service.GetBySession(ParseSessionId(request.TableSessionId), context.CancellationToken));
    }

    public override async Task<BillingOrderSnapshot> GetForPaymentReview(BillingOrderRequest request, ServerCallContext context)
    {
        var id = ParseSessionId(request.TableSessionId);
        await access.RequireAsync(id, null, string.Empty, context.CancellationToken);
        var restaurantId = scope.RestaurantId
            ?? throw new RpcException(new Status(StatusCode.PermissionDenied, "Không xác định được phạm vi nhà hàng."));
        return Map(await service.GetForRestaurant(id, restaurantId, context.CancellationToken));
    }

    public override async Task<OrderReportChannelsReply> GetReportChannels(OrderReportChannelsRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.RestaurantId, out var restaurantId)
            || request.OrderIds.Any(value => !Guid.TryParse(value, out _)))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã nhà hàng hoặc đơn không hợp lệ."));
        }

        var values = await service.GetReportChannels(restaurantId, request.OrderIds.Select(Guid.Parse).ToArray(), context.CancellationToken);
        var reply = new OrderReportChannelsReply
        {
            RestaurantId = restaurantId.ToString()
        };
        reply.Items.AddRange(values.Select(value => new OrderReportChannelInfo
        {
            OrderId = value.OrderId.ToString(),
            Channel = value.Channel
        }));
        return reply;
    }

    public override async Task<ReportOrdersReply> GetReportItems(OrderReportChannelsRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.RestaurantId, out var restaurantId)
            || request.OrderIds.Any(value => !Guid.TryParse(value, out _)))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã nhà hàng hoặc đơn không hợp lệ."));
        }

        var values = await service.GetReportItems(restaurantId, request.OrderIds.Select(Guid.Parse).ToArray(), context.CancellationToken);
        var reply = new ReportOrdersReply
        {
            RestaurantId = restaurantId.ToString()
        };
        reply.Items.AddRange(values.Select(order =>
        {
            var snapshot = new ReportOrderSnapshotInfo
            {
                OrderId = order.OrderId.ToString(),
                Channel = order.Channel,
                Status = order.Status
            };
            snapshot.Items.AddRange(order.Items.Select(item =>
            {
                var line = new ReportOrderItemInfo
                {
                    OrderItemId = item.OrderItemId.ToString(),
                    ItemId = item.ItemId.ToString(),
                    Name = item.Name,
                    UnitPrice = item.UnitPrice.ToString(CultureInfo.InvariantCulture),
                    Quantity = item.Quantity,
                    CancelledQuantity = item.CancelledQuantity,
                    Status = item.Status
                };
                line.Options.AddRange(item.Options.Select(option => new QuotedOptionInfo
                {
                    OptionId = option.OptionId.ToString(),
                    Name = option.Name,
                    Quantity = option.Quantity,
                    ExtraPrice = option.ExtraPrice.ToString(CultureInfo.InvariantCulture)
                }));
                return line;
            }));
            return snapshot;
        }));
        return reply;
    }

    private static Guid ParseSessionId(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã phiên bàn không hợp lệ hoặc bị trống."));
        }

        return id;
    }

    private static BillingOrderSnapshot Map(BillingOrderSnapshotDTO source)
    {
        var reply = new BillingOrderSnapshot
        {
            TableSessionId = source.TableSessionId.ToString(),
            RestaurantId = source.RestaurantId.ToString(),
            PendingOrderCount = source.PendingOrderCount
        };
        reply.Items.AddRange(source.Items.Select(line =>
        {
            var value = new BillingOrderLine
            {
                OrderId = line.OrderId.ToString(),
                OrderItemId = line.OrderItemId.ToString(),
                ItemId = line.ItemId.ToString(),
                OriginalTableId = line.OriginalTableId.ToString(),
                Name = line.Name,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice.ToString(CultureInfo.InvariantCulture),
                Status = line.Status
            };
            if (line.ComboGroupId.HasValue)
            {
                value.ComboGroupId = line.ComboGroupId.Value.ToString();
            }

            value.Options.AddRange(line.Options.Select(option => new QuotedOptionInfo
            {
                OptionId = option.OptionId.ToString(),
                Name = option.Name,
                Quantity = option.Quantity,
                ExtraPrice = option.ExtraPrice.ToString(CultureInfo.InvariantCulture)
            }));
            return value;
        }));
        return reply;
    }
}
