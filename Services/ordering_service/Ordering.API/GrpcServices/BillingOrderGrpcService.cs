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
