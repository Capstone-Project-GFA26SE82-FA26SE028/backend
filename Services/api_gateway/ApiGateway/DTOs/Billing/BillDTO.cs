namespace ApiGateway.DTOs.Billing;

public sealed class BillDTO
{
    public Guid BillId { get; set; }
    public Guid RestaurantId { get; set; }
    public string BillNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal VatRateSnapshot { get; set; }
    public decimal ServiceRateSnapshot { get; set; }
    public decimal VatAmount { get; set; }
    public decimal ServiceAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal RoundingAdjustment { get; set; }
    public string SourceStatus { get; set; } = "NotChecked";
    public string Version { get; set; } = string.Empty;
    public bool HasCompleteCogs { get; set; }
    public List<string> Warnings { get; set; } = [];
    public List<BillLineDTO> Items { get; set; } = [];
}

public sealed class BillLineDTO
{
    public Guid BillLineId { get; set; }
    public Guid OrderItemRef { get; set; }
    public Guid TableSessionRef { get; set; }
    public Guid OriginalTableRef { get; set; }
    public Guid? ComboGroupRef { get; set; }
    public BillItemSnapshot Item { get; set; } = new();
    public decimal Quantity { get; set; }
    public decimal UnitPriceSnapshot { get; set; }
    public decimal? CogsSnapshot { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class BillItemSnapshot
{
    public string SourceFingerprint { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public Guid ItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal BaseUnitPrice { get; set; }
    public List<BillOptionSnapshot> Options { get; set; } = [];
}

public sealed class BillOptionSnapshot
{
    public Guid OptionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal ExtraPrice { get; set; }
}
