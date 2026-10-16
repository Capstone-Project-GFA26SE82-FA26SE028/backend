namespace Restaurant.Common.DTOs;

public class TableWriteDto
{
    public Guid AreaId { get; set; }
    public string TableCode { get; set; } = "";
    public int Capacity { get; set; }
    public decimal? PositionX { get; set; }
    public decimal? PositionY { get; set; }
    public string Version { get; set; } = "";
}
