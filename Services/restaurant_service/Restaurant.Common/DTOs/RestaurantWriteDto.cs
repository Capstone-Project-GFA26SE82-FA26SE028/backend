namespace Restaurant.Common.DTOs;

public class RestaurantWriteDto
{
    public string RestaurantName { get; set; } = "";
    public string Address { get; set; } = "";
    public string Hotline { get; set; } = "";
    public string? TaxCode { get; set; }
    public string? LogoUrl { get; set; }
    public string TimeZone { get; set; } = "Asia/Ho_Chi_Minh";
    public List<OpeningHourDto> OpeningHours { get; set; } = [];
}
