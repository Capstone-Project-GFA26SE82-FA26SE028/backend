namespace ApiGateway.DTOs.Restaurant;

public sealed class StatusDto
{
    public bool IsActive { get; set; }
    public string Version { get; set; } = "";
}
