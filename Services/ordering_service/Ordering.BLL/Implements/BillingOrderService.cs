using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;
using Ordering.DAL.Interfaces;

namespace Ordering.BLL.Implements;

public sealed class BillingOrderService(IOrderRepository orders, IBillingSessionClient sessions, IOrderReportAccess reportAccess) : IBillingOrderService
{
    public async Task<IReadOnlyList<OrderReportChannelDTO>> GetReportChannels(Guid restaurantId, Guid[] orderIds, CancellationToken ct)
    {
        await RequireReportAccess(restaurantId, orderIds, ct);
        return await orders.GetReportChannelsAsync(restaurantId, orderIds, ct);
    }

    public async Task<IReadOnlyList<OrderReportSnapshotDTO>> GetReportItems(Guid restaurantId, Guid[] orderIds, CancellationToken ct)
    {
        await RequireReportAccess(restaurantId, orderIds, ct);
        return await orders.GetReportItemsAsync(restaurantId, orderIds, ct);
    }

    private async Task RequireReportAccess(Guid restaurantId, Guid[] orderIds, CancellationToken ct)
    {
        if (restaurantId == Guid.Empty || orderIds.Length is < 1 or > 100
            || orderIds.Any(value => value == Guid.Empty) || orderIds.Distinct().Count() != orderIds.Length)
        {
            throw new OrderingException(400, "Yêu cầu nhà hàng hợp lệ và từ 1 đến 100 mã đơn khác nhau.");
        }

        await reportAccess.RequireManager(restaurantId, ct);
    }

    public async Task<BillingOrderSnapshotDTO> GetBySession(Guid sessionId, CancellationToken ct)
    {
        if (sessionId == Guid.Empty)
        {
            throw new OrderingException(400, "Mã phiên bàn không hợp lệ hoặc bị trống.");
        }

        var restaurantId = await sessions.GetRestaurantIdAsync(sessionId, ct);
        return await GetForRestaurant(sessionId, restaurantId, ct);
    }

    public async Task<BillingOrderSnapshotDTO> GetForRestaurant(Guid sessionId, Guid restaurantId, CancellationToken ct)
    {
        var source = await orders.GetForBillingAsync(sessionId, ct);
        var result = new BillingOrderSnapshotDTO
        {
            TableSessionId = sessionId,
            RestaurantId = restaurantId,
            PendingOrderCount = source.Count(order => order.Status == "PendingConfirmation")
        };
        foreach (var order in source.Where(order => order.Status is "Confirmed" or "Ready" or "Served"))
        {
            foreach (var item in order.OrderItems)
            {
                if (item.CancelledQuantity < 0 || item.CancelledQuantity > item.Quantity)
                {
                    throw new OrderingException(409, "Món ăn có số lượng hủy không nhất quán.");
                }

                if (item.Status == "Cancelled" || item.Quantity == item.CancelledQuantity)
                {
                    continue;
                }

                if (!item.OriginalTableRef.HasValue || item.OriginalTableRef == Guid.Empty)
                {
                    throw new OrderingException(409, "Món ăn bị thiếu thông tin bàn phục vụ gốc.");
                }

                result.Items.Add(new BillingOrderLineDTO
                {
                    OrderId = order.OrderId,
                    OrderItemId = item.OrderItemId,
                    ItemId = item.ItemRef,
                    OriginalTableId = item.OriginalTableRef.Value,
                    ComboGroupId = item.ComboGroupId,
                    Name = item.NameSnapshot,
                    Quantity = item.Quantity - item.CancelledQuantity,
                    UnitPrice = item.UnitPriceSnapshot,
                    Status = item.Status,
                    Options = item.OrderItemOptions.OrderBy(option => option.OptionRef).Select(option => new QuotedOptionDTO
                    {
                        OptionId = option.OptionRef,
                        Name = option.NameSnapshot,
                        Quantity = option.Quantity,
                        ExtraPrice = option.ExtraPriceSnapshot
                    }).ToList()
                });
            }
        }

        return result;
    }
}
