namespace Billing.Common.DTOs;

public sealed record ReportOrderSnapshotDTO(Guid OrderId, string Channel, string Status, IReadOnlyList<ReportOrderItemDTO> Items);

public sealed record ReportOrderItemDTO(Guid OrderItemId, Guid ItemId, string Name, decimal UnitPrice,
    int Quantity, int CancelledQuantity, string Status, IReadOnlyList<BillOptionSnapshot> Options);
