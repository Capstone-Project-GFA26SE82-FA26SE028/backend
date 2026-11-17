using Microsoft.EntityFrameworkCore;
using Shared.Common.Notifications;
using Shared.Persistence.Notifications;

namespace Billing.DAL.Models;

public partial class BillingDbContext
{
    protected override async Task CaptureNotificationEventsAsync(CancellationToken ct)
    {
        foreach (var entry in ChangeTracker.Entries<BillCheckout>().Where(value => value.State == EntityState.Added).ToArray())
        {
            var checkout = entry.Entity;
            var restaurantId = entry.Property("RestaurantId").CurrentValue as Guid? ?? CurrentRestaurantId;
            if (restaurantId.HasValue)
            {
                await NotificationOutboxModel.Enqueue(this, new NotificationEvent
                {
                    EventKey = $"Billing:CheckoutRequested:{checkout.CheckoutId:N}",
                    Type = "CheckoutRequested",
                    RestaurantId = restaurantId.Value,
                    EntityId = checkout.TableSessionRef
                }, ct);
            }
        }
    }
}
