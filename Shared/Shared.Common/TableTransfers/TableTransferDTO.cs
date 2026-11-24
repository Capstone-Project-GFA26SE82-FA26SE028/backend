namespace Shared.Common.TableTransfers;

public sealed class TableTransferDTO
{
    public Guid OperationId { get; set; }
    public Guid SourceSessionId { get; set; }
    public Guid TargetTableId { get; set; }
    public string Mode { get; set; } = "";
    public string Reason { get; set; } = "";
    public List<TableTransferLineDTO> Items { get; set; } = [];
}

public sealed class TableTransferLineDTO
{
    public Guid OrderItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class TableTransferResultDTO
{
    public Guid OperationId { get; set; }
    public Guid SourceSessionId { get; set; }
    public Guid TargetSessionId { get; set; }
    public Guid TargetTableId { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid ActorId { get; set; }
    public string Status { get; set; } = "";
}
