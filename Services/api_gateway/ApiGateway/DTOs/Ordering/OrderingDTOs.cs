using System.Text.Json.Serialization;

namespace ApiGateway.DTOs.Ordering;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SelectionDTO
{
    public Guid ItemId { get; set; }
    public int Quantity { get; set; }
    public string? Note { get; set; }
    public List<OptionSelectionDTO> Options { get; set; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class OptionSelectionDTO
{
    public Guid OptionId { get; set; }
    public int Quantity { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CartWriteDTO
{
    public string Version { get; set; } = string.Empty;
    public List<SelectionDTO> Items { get; set; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateOrderDTO
{
    public Guid? CartId { get; set; }
    public string? CartVersion { get; set; }
    public Guid? TableId { get; set; }
    public List<SelectionDTO> Items { get; set; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ConfirmOrderDTO
{
    public string Version { get; set; } = string.Empty;
}

public sealed class CartDTO
{
    public Guid CartId { get; set; }
    public Guid TableSessionId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string Version { get; set; } = string.Empty;
    public List<SelectionDTO> Items { get; set; } = [];
    public QuoteDTO? Quote { get; set; }
}

public sealed class QuoteDTO
{
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset EvaluatedAt { get; set; }
    public decimal? TotalAmount { get; set; }
    public List<QuotedItemDTO> Items { get; set; } = [];
    public List<QuoteErrorDTO> Errors { get; set; } = [];
}

public sealed class QuoteErrorDTO
{
    public int LineIndex { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class QuotedItemDTO
{
    public Guid? KitchenStationId { get; set; }
    public Guid ItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid RecipeVersionId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public List<QuotedOptionDTO> Options { get; set; } = [];
}

public sealed class QuotedOptionDTO
{
    public Guid OptionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal ExtraPrice { get; set; }
}

public sealed class OrderDTO
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid TableSessionId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? ConfirmedBy { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public decimal TotalAmount { get; set; }
    public List<OrderLineDTO> Items { get; set; } = [];
}

public sealed class OrderLineDTO
{
    public Guid OrderItemId { get; set; }
    public Guid ItemId { get; set; }
    public Guid? RecipeVersionId { get; set; }
    public string NameSnapshot { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPriceSnapshot { get; set; }
    public decimal LineTotal { get; set; }
    public string? Note { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<QuotedOptionDTO> Options { get; set; } = [];
}
