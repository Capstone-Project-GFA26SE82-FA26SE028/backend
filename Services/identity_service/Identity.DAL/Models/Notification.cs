namespace Identity.Domain.Models;

public sealed class Notification
{
    public Guid NotificationId { get; set; }
    public Guid RestaurantId { get; set; }
    public string EventKey { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool Suppressed { get; set; }
    public ICollection<NotificationRecipient> Recipients { get; set; } = new List<NotificationRecipient>();
}
