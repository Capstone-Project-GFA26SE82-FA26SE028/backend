using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;
using Ordering.DAL.Interfaces;

namespace Ordering.BLL.Implements;

public sealed class BillingOrderService(IOrderRepository orders, IBillingSessionClient sessions) : IBillingOrderService
{
    public async Task<BillingOrderSnapshotDTO> GetBySession(Guid sessionId, CancellationToken ct)
    {
        if (sessionId == Guid.Empty)
        {
            throw new OrderingException(400, "A valid table session ID is required.");
        }

        var branch = await sessions.GetRestaurantIdAsync(sessionId, ct);
        var source = await orders.GetForBillingAsync(sessionId, ct);
        var result = new BillingOrderSnapshotDTO
        {
            TableSessionId = sessionId,
            RestaurantId = branch,
            PendingOrderCount = source.Count(order => order.Status == "PendingConfirmation")
        };
        foreach (var order in source.Where(order => order.Status is "Confirmed" or "Ready" or "Served"))
        {
            foreach (var item in order.OrderItems)
            {
                if (item.CancelledQuantity < 0 || item.CancelledQuantity > item.Quantity)
                {
                    throw new OrderingException(409, "An order item has inconsistent cancelled quantities.");
                }

                if (item.Status == "Cancelled" || item.Quantity == item.CancelledQuantity)
                {
                    continue;
                }

                if (!item.OriginalTableRef.HasValue || item.OriginalTableRef == Guid.Empty)
                {
                    throw new OrderingException(409, "An order item is missing its original table reference.");
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
