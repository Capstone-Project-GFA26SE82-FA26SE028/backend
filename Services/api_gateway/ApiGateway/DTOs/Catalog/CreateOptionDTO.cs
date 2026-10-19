namespace ApiGateway.DTOs.Catalog;

public sealed class CreateOptionDTO
{
    public Guid OptionGroupId { get; set; }
    public string? OptionName { get; set; }
    public decimal? ExtraPrice { get; set; }
}
