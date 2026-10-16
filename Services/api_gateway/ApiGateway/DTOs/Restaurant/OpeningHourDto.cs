namespace ApiGateway.DTOs.Restaurant;

public class OpeningHourDto
{
    public int DayOfWeek { get; set; }
    public string OpenAt { get; set; } = "";
    public string CloseAt { get; set; } = "";
}
