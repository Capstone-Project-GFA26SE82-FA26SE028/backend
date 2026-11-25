namespace Restaurant.DAL.Models;

public sealed class TableTransferSession
{

    public Guid OperationId { get; set; }
    public Guid SourceSessionId { get; set; }
    public Guid TargetSessionId { get; set; }
    public Guid TargetTableId { get; set; }
    public Guid ActorId { get; set; }
    public Guid RestaurantId { get; set; }
    public string RequestHash { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool TargetCreated { get; set; }
}
