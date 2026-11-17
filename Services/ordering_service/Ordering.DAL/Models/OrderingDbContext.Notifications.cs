using Shared.Persistence.Notifications;

namespace Ordering.DAL.Models;

public partial class OrderingDbContext
{
    protected override bool IsRestaurantEntity(Type type)
    {
        return type != typeof(NotificationOutbox);
    }
}
