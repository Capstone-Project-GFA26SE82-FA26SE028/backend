namespace Shared.Common.Notifications;

public sealed class NotificationQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Type { get; set; }
    public bool? IsRead { get; set; }
    public Guid? EntityId { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
}

public sealed class AnnouncementWrite
{
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public bool AllStaff { get; set; }
    public string[] RecipientRoleIds { get; set; } = [];
    public Guid? Version { get; set; }
}

public sealed class NotificationEvent
{
    public string EventKey { get; set; } = "";
    public string Type { get; set; } = "";
    public Guid RestaurantId { get; set; }
    public Guid EntityId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? KitchenStationId { get; set; }
    public Guid? KitchenUserId { get; set; }
    public DateTime OccurredAt { get; set; }
}

public sealed record NotificationItem(Guid Id, Guid RestaurantId, string Type, string Title, string Message,
    string EntityType, Guid EntityId, DateTime CreatedAt, DateTime? ReadAt);
public sealed record NotificationReader(Guid UserId, string FullName, DateTime? ReadAt);
public sealed record NotificationDetails(NotificationItem Notification, IReadOnlyList<NotificationReader> Recipients);
public sealed record AnnouncementItem(Guid Id, Guid RestaurantId, string Title, string Message, bool AllStaff,
    string[] RecipientRoleIds, Guid Version, string Status, DateTime CreatedAt, DateTime? SentAt, Guid? NotificationId);
public sealed record NotificationSettingItem(string Type, string Title, bool Required, bool Enabled, string[] RecipientRoles, bool Customer);

public sealed class NotificationException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed record NotificationDefinition(string Title, string Message, string EntityType, bool Customer, string[] Roles, bool Required = true);

public static class NotificationCatalog
{
    public static readonly string[] StaffRoles = ["manager", "waiter", "cashier", "kitchen", "inventory"];
    public static readonly IReadOnlyDictionary<string, NotificationDefinition> Types = new Dictionary<string, NotificationDefinition>(StringComparer.Ordinal)
    {
        ["DineInOrderSubmitted"] = new("Có đơn tại bàn chờ duyệt", "Khách vừa gửi món mới. Vui lòng kiểm tra đơn hàng.", "Order", false, ["waiter"]),
        ["KitchenOrderAccepted"] = new("Có món cần chế biến", "Món đã được xác nhận và chuyển đến trạm bếp phụ trách.", "Order", false, ["kitchen"]),
        ["DineInItemReady"] = new("Món đã sẵn sàng phục vụ", "Bếp đã hoàn tất món. Vui lòng kiểm tra đơn để phục vụ khách.", "Order", false, ["waiter"]),
        ["ItemCancellationRequested"] = new("Có yêu cầu hủy món", "Khách yêu cầu hủy món. Vui lòng kiểm tra và xử lý.", "Order", false, ["waiter"]),
        ["CheckoutRequested"] = new("Có yêu cầu thanh toán", "Khách yêu cầu thanh toán tại bàn. Vui lòng kiểm tra phiên bàn.", "TableSession", false, ["waiter"]),
        ["TakeawayPaymentConfirmed"] = new("Thanh toán thành công", "Đơn hàng đã thanh toán và đang chờ nhà hàng xác nhận.", "Order", true, []),
        ["TakeawayAwaitingAcceptance"] = new("Có đơn mang đi chờ duyệt", "Đơn mang đi đã thanh toán. Vui lòng xác nhận hoặc từ chối đơn.", "Order", false, ["waiter"]),
        ["TakeawayAccepted"] = new("Nhà hàng đã nhận đơn", "Nhà hàng đã nhận đơn mang đi của bạn.", "Order", true, []),
        ["TakeawayRejected"] = new("Nhà hàng đã từ chối đơn", "Vui lòng mở đơn để xem lý do và theo dõi hoàn tiền.", "Order", true, []),
        ["TakeawayCancelled"] = new("Đã hủy đơn hàng", "Đơn hàng đã hủy. Vui lòng mở đơn để xem trạng thái thanh toán và hoàn tiền.", "Order", true, []),
        ["TakeawayReady"] = new("Đơn hàng đã sẵn sàng", "Đơn mang đi đã sẵn sàng. Khách vui lòng đến nhà hàng nhận món.", "Order", true, ["waiter"]),
        ["RefundPending"] = new("Đơn hàng đang chờ hoàn tiền", "Vui lòng mở đơn để cung cấp hoặc kiểm tra thông tin nhận hoàn tiền.", "Order", true, ["cashier"]),
        ["RefundAccountSubmitted"] = new("Đã có tài khoản nhận hoàn tiền", "Khách đã cung cấp thông tin nhận hoàn tiền. Vui lòng mở đơn để xử lý.", "Order", false, ["cashier"]),
        ["RefundConfirmed"] = new("Nhà hàng xác nhận đã hoàn tiền", "Nhà hàng đã xác nhận hoàn tiền. Vui lòng mở đơn để xem chi tiết.", "Order", true, []),
        ["Announcement"] = new("Thông báo nội bộ", "", "Announcement", false, StaffRoles, false)
    };
}
