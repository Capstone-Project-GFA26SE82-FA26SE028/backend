using Backend.Contracts.Restaurant;
using Grpc.Core;
using Identity.BLL.Interfaces;
using Shared.Common.Notifications;

namespace Identity.API.Clients;

public sealed class NotificationAudience(RestaurantStaffGrpc.RestaurantStaffGrpcClient client, IConfiguration configuration) : INotificationAudience
{
    public async Task<IReadOnlySet<Guid>> GetStaffAsync(Guid restaurantId, CancellationToken ct)
    {
        var key = configuration["Notifications:ServiceKey"];
        if (string.IsNullOrEmpty(key) || key.Length < 32)
        {
            throw new NotificationException(503, "Chưa cấu hình khóa nội bộ cho thông báo.");
        }

        try
        {
            var result = await client.GetNotificationRecipientsAsync(new RestaurantStaffListRequest
            {
                RestaurantId = restaurantId.ToString()
            }, new Metadata { { "x-notification-key", key } }, DateTime.UtcNow.AddSeconds(5), ct);
            return result.Items.Where(value => value.IsActive && value.RestaurantId == restaurantId.ToString()
                && Guid.TryParse(value.UserIdRef, out _)).Select(value => Guid.Parse(value.UserIdRef)).ToHashSet();
        }
        catch (RpcException)
        {
            throw new NotificationException(503, "Chưa xác minh được nhân viên nhận thông báo; vui lòng thử lại.");
        }
    }
}
