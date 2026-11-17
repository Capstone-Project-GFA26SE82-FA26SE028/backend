using Microsoft.EntityFrameworkCore;
using Ordering.DAL.Models;
using RestaurantIsolation;
using Shared.Common.Notifications;

namespace Ordering.API.Clients;

public sealed class NotificationEventEnricher(OrderingDbContext db) : INotificationEventEnricher
{
    public async Task Enrich(NotificationEvent value, CancellationToken ct)
    {
        if (value.Type != "KitchenOrderAccepted")
        {
            return;
        }

        value.KitchenUserId = await db.KitchenAssignments
            .Where(assignment => assignment.KitchenStationId == value.KitchenStationId && assignment.KitchenStation.IsActive)
            .Select(assignment => (Guid?)assignment.UserRef).SingleOrDefaultAsync(ct);
        if (!value.KitchenUserId.HasValue)
        {
            throw new InvalidOperationException("Chưa có nhân viên phụ trách trạm bếp; thông báo sẽ được thử lại.");
        }
    }
}
