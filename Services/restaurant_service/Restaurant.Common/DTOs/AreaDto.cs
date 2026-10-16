namespace Restaurant.Common.DTOs;

public sealed class AreaDto : AreaWriteDto
{
    public Guid AreaId { get; set; }
    public bool IsActive { get; set; }
}
