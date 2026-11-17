namespace Identity.Domain.Models;

public sealed class NotificationRecipient
{
    public Guid NotificationId { get; set; }
    public Guid UserId { get; set; }
    public bool IsCustomer { get; set; }
    public DateTime? ReadAt { get; set; }
    public Notification Notification { get; set; } = null!;
}
