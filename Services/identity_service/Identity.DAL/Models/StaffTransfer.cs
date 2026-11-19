namespace Identity.Domain.Models;

public sealed class StaffTransfer
{
    public Guid StaffTransferId { get; set; }
    public Guid UserId { get; set; }
    public Guid SourceRestaurantId { get; set; }
    public Guid TargetRestaurantId { get; set; }
    public Guid RequestedBy { get; set; }
    public bool WasActive { get; set; }
    public string Status { get; set; } = "Pending";
    public string? Message { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
