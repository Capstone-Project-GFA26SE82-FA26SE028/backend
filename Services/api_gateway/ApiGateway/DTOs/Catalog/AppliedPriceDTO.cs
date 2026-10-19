namespace ApiGateway.DTOs.Catalog;

public sealed class AppliedPriceDTO
{
    public Guid ItemId { get; set; }
    public Guid PriceId { get; set; }
    public decimal Amount { get; set; }
    public string RequestedChannel { get; set; } = string.Empty;
    public string PriceChannel { get; set; } = string.Empty;
    public DateTimeOffset EvaluatedAt { get; set; }
    public bool HasValidStandardPrice { get; set; }
    public PriceDTO Price { get; set; } = new();
}
