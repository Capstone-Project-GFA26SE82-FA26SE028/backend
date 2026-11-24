using Shared.Common.TableTransfers;

namespace ApiGateway.DTOs.Ordering;

public sealed class TableSplitRequest
{
    public Guid TargetTableId { get; set; }
    public string Reason { get; set; } = "";
    public List<TableTransferLineDTO> Items { get; set; } = [];
}

public sealed class TableMergeRequest
{
    public Guid TargetTableId { get; set; }
    public string Reason { get; set; } = "";
}
