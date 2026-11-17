namespace Identity.Domain.Models;

public sealed class NotificationSetting
{
    public Guid RestaurantId { get; set; }
    public string Type { get; set; } = "";
    public bool Enabled { get; set; } = true;
}
