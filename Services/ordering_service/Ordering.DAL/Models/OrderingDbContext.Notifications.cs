using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Shared.Common.Notifications;
using Shared.Persistence.Notifications;

namespace Ordering.DAL.Models;

public partial class OrderingDbContext
{
    protected override bool IsRestaurantEntity(Type type)
    {
        return type != typeof(NotificationOutbox);
    }

    protected override async Task CaptureNotificationEventsAsync(CancellationToken ct)
    {
        foreach (var entry in ChangeTracker.Entries<Order>().Where(value => value.State is EntityState.Added or EntityState.Modified).ToArray())
        {
            var order = entry.Entity;
            if (order.CustomerUserRef.HasValue && order.Channel == "Takeaway")
            {
                if (Changed(entry, nameof(Order.Status)))
                {
                    var type = order.Status switch
                    {
                        "PendingConfirmation" => "TakeawayAwaitingAcceptance",
                        "Accepted" => "TakeawayAccepted",
                        "Rejected" => "TakeawayRejected",
                        "Cancelled" => "TakeawayCancelled",
                        "ReadyForPickup" => "TakeawayReady",
                        _ => null
                    };
                    if (type is not null)
                    {
                        await OrderEvent(order, type, order.OrderId, ct);
                    }
                }
                if (Changed(entry, nameof(Order.PaymentStatus)))
                {
                    var type = order.PaymentStatus switch
                    {
                        "Paid" => "TakeawayPaymentConfirmed",
                        "RefundPending" => "RefundPending",
                        "Refunded" => "RefundConfirmed",
                        _ => null
                    };
                    if (type is not null)
                    {
                        await OrderEvent(order, type, order.OrderId, ct);
                    }
                }
                if (Changed(entry, nameof(Order.RefundAccountNumber)) && !string.IsNullOrEmpty(order.RefundAccountNumber))
                {
                    await OrderEvent(order, "RefundAccountSubmitted", order.OrderId, ct);
                }
            }
            else if (entry.State == EntityState.Added && order.Status == "PendingConfirmation")
            {
                await OrderEvent(order, "DineInOrderSubmitted", order.OrderId, ct);
            }
        }

        foreach (var entry in ChangeTracker.Entries<KitchenTask>().Where(value => value.State is EntityState.Added or EntityState.Modified).ToArray())
        {
            var task = entry.Entity;
            if (entry.State != EntityState.Added && !(Changed(entry, nameof(KitchenTask.Status)) && task.Status == "Ready"))
            {
                continue;
            }
            var item = task.OrderItem ?? await OrderItems.FindAsync([task.OrderItemId], ct);
            var order = item?.Order ?? (item is null ? null : await Orders.FindAsync([item.OrderId], ct));
            if (order is null)
            {
                continue;
            }
            if (entry.State == EntityState.Added)
            {
                await OrderEvent(order, "KitchenOrderAccepted", task.KitchenTaskId, ct, task.KitchenStationId);
            }
            if (task.Status == "Ready" && !order.CustomerUserRef.HasValue)
            {
                await OrderEvent(order, "DineInItemReady", task.KitchenTaskId, ct);
            }
        }

        foreach (var entry in ChangeTracker.Entries<CancellationRequest>().Where(value => value.State == EntityState.Added).ToArray())
        {
            if (entry.Entity.Status != "Pending")
            {
                continue;
            }
            var item = entry.Entity.OrderItem ?? await OrderItems.FindAsync([entry.Entity.OrderItemId], ct);
            var order = item?.Order ?? (item is null ? null : await Orders.FindAsync([item.OrderId], ct));
            if (order is not null)
            {
                await OrderEvent(order, "ItemCancellationRequested", entry.Entity.CancellationRequestId, ct);
            }
        }
    }

    private Task OrderEvent(Order order, string type, Guid occurrence, CancellationToken ct, Guid? kitchenStationId = null)
    {
        return NotificationOutboxModel.Enqueue(this, new NotificationEvent
        {
            EventKey = $"Ordering:{type}:{occurrence:N}",
            Type = type,
            RestaurantId = order.RestaurantRef ?? CurrentRestaurantId ?? Guid.Empty,
            EntityId = order.OrderId,
            CustomerId = order.CustomerUserRef,
            KitchenStationId = kitchenStationId
        }, ct);
    }

    private static bool Changed<T>(EntityEntry<T> entry, string property) where T : class
    {
        return entry.State == EntityState.Added || entry.Property(property).IsModified
            && !Equals(entry.Property(property).OriginalValue, entry.Property(property).CurrentValue);
    }
}
