namespace ApiGateway.DTOs.Restaurant;

public sealed class AreaDto : AreaWriteDto
{
    public Guid AreaId { get; set; }
    public bool IsActive { get; set; }
}
