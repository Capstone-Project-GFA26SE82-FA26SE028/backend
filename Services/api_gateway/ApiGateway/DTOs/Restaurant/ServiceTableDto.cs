namespace ApiGateway.DTOs.Restaurant;

public sealed class ServiceTableDto
{
    public string Version { get; set; } = string.Empty;
    public Guid TableId { get; set; }
    public string TableCode { get; set; } = string.Empty;
    public Guid AreaId { get; set; }
    public string AreaName { get; set; } = string.Empty;
    public Guid RestaurantId { get; set; }
    public string CleaningStatus { get; set; } = string.Empty;
    public List<Guid> OpenSessionIds { get; set; } = [];
}
