namespace Identity.Domain.Models;

public sealed class Announcement
{
    public Guid AnnouncementId { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid CreatedBy { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public bool AllStaff { get; set; }
    public string RecipientRoles { get; set; } = "";
    public Guid Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public Guid? NotificationId { get; set; }
}
