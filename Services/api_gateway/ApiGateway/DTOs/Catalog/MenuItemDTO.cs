namespace ApiGateway.DTOs.Catalog;

public sealed class MenuItemDTO
{
    public Guid ItemId { get; set; }
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string Availability { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public Guid? PriceId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public DateTimeOffset EvaluatedAt { get; set; }
    public bool CanOrder { get; set; }
    public string? UnavailableReason { get; set; }
    public List<MenuOptionGroupDTO> OptionGroups { get; set; } = [];
}

public sealed class MenuOptionGroupDTO
{
    public Guid OptionGroupId { get; set; }
    public string OptionGroupName { get; set; } = string.Empty;
    public int MinSelection { get; set; }
    public int MaxSelection { get; set; }
    public List<MenuOptionDTO> Options { get; set; } = [];
}

public sealed class MenuOptionDTO
{
    public Guid OptionId { get; set; }
    public string OptionName { get; set; } = string.Empty;
    public decimal ExtraPrice { get; set; }
    public string Availability { get; set; } = string.Empty;
    public bool CanSelect { get; set; }
}
