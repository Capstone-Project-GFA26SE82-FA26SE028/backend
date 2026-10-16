namespace Restaurant.Common.DTOs;

public sealed class StatusDto
{
    public bool IsActive { get; set; }
    public string Version { get; set; } = "";
}
