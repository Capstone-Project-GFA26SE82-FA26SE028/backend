namespace Restaurant.DAL.Models;

public sealed class StaffTransferReceipt
{
    public Guid StaffTransferReceiptId { get; set; }
    public Guid UserId { get; set; }
    public Guid SourceRestaurantId { get; set; }
    public Guid TargetRestaurantId { get; set; }
    public string Status { get; set; } = "Completed";
    public string? Message { get; set; }
    public DateTime CreatedAt { get; set; }
}
