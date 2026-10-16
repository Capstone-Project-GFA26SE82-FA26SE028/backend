namespace Restaurant.Common.DTOs;

public class OpeningHourDto
{
    public int DayOfWeek { get; set; }
    public string OpenAt { get; set; } = "";
    public string CloseAt { get; set; } = "";
}
