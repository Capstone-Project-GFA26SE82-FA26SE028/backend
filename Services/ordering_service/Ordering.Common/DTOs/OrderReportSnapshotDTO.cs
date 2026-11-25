namespace Ordering.Common.DTOs;

public sealed record OrderReportSnapshotDTO(Guid OrderId, string Channel, string Status, IReadOnlyList<OrderReportItemDTO> Items);

public sealed record OrderReportItemDTO(Guid OrderItemId, Guid ItemId, string Name, decimal UnitPrice,
    int Quantity, int CancelledQuantity, string Status, IReadOnlyList<OrderReportOptionDTO> Options);

public sealed record OrderReportOptionDTO(Guid OptionId, string Name, int Quantity, decimal ExtraPrice);
