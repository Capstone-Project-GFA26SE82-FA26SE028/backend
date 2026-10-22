namespace Inventory.DAL.Models;

public sealed class StockIssue
{
    public Guid StockIssueId { get; set; }
    public Guid CreatedByRef { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Reason { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public ICollection<StockMovement> Movements { get; set; } = new List<StockMovement>();
}
