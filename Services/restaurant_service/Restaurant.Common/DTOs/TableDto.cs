namespace Restaurant.Common.DTOs;

public sealed class TableDto : TableWriteDto
{
    public Guid TableId { get; set; }
    public bool IsActive { get; set; }
    public string CleaningStatus { get; set; } = "";
}
