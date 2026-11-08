namespace Ordering.Common.DTOs;

public sealed class KitchenStationWriteDTO
{
    public string KitchenStationCode { get; set; } = string.Empty;
    public string KitchenStationName { get; set; } = string.Empty;
}

public sealed class KitchenStationDTO
{
    public Guid KitchenStationId { get; set; }
    public string KitchenStationCode { get; set; } = string.Empty;
    public string KitchenStationName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class KitchenStationPageDTO
{
    public List<KitchenStationDTO> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
