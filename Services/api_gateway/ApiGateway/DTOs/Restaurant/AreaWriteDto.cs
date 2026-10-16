namespace ApiGateway.DTOs.Restaurant;

public class AreaWriteDto
{
    public Guid RestaurantId { get; set; }
    public string AreaCode { get; set; } = "";
    public string AreaName { get; set; } = "";
    public string ServiceType { get; set; } = "";
    public string SpaceType { get; set; } = "";
}
