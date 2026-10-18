namespace ApiGateway.DTOs.Restaurant;

public sealed class TableSessionScanDto
{
    public Guid TableSessionId { get; set; }
    public Guid TableId { get; set; }
    public string TableCode { get; set; } = "";
    public Guid AreaId { get; set; }
    public Guid RestaurantId { get; set; }
    public string Status { get; set; } = "";
    public DateTime OpenedAt { get; set; }
    public bool IsNewSession { get; set; }
}
