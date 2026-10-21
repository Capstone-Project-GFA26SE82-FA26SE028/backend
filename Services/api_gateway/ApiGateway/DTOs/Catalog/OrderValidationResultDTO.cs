namespace ApiGateway.DTOs.Catalog;

public sealed class OrderValidationResultDTO
{
    public string Channel { get; set; } = string.Empty;
    public DateTimeOffset EvaluatedAt { get; set; }
    public decimal? TotalAmount { get; set; }
    public List<ValidatedOrderItemDTO> Items { get; set; } = [];
    public List<OrderValidationErrorDTO> Errors { get; set; } = [];
}

public sealed class ValidatedOrderItemDTO
{
    public int LineIndex { get; set; }
    public Guid? KitchenStationId { get; set; }
    public Guid ItemId { get; set; }
    public string NameSnapshot { get; set; } = string.Empty;
    public Guid PriceId { get; set; }
    public Guid RecipeVersionId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPriceSnapshot { get; set; }
    public decimal OptionsTotalPerUnit { get; set; }
    public decimal UnitTotal { get; set; }
    public decimal LineTotal { get; set; }
    public List<ValidatedOrderOptionDTO> Options { get; set; } = [];
}

public sealed class ValidatedOrderOptionDTO
{
    public Guid OptionId { get; set; }
    public string NameSnapshot { get; set; } = string.Empty;
    public decimal ExtraPriceSnapshot { get; set; }
    public int Quantity { get; set; }
    public Guid? RecipeVersionId { get; set; }
}

public sealed class OrderValidationErrorDTO
{
    public int LineIndex { get; set; }
    public Guid ItemId { get; set; }
    public Guid? OptionId { get; set; }
    public Guid? OptionGroupId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
