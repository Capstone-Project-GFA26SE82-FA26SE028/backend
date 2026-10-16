namespace Restaurant.Common.DTOs;

public sealed class QrTableDto
{
    public Guid TableId { get; set; }
    public string TableCode { get; set; } = "";
    public Guid AreaId { get; set; }
    public Guid RestaurantId { get; set; }
}
