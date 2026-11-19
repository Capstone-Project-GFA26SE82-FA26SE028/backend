namespace Ordering.Common.DTOs;

public sealed class KitchenEmployeeDTO
{
    public Guid UserId { get; set; }
    public string? FullName { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? AvatarUrl { get; set; }
    public string RoleId { get; set; } = "";
    public bool IsActive { get; set; }
}

public sealed class KitchenAssignmentDTO
{
    public Guid KitchenStationId { get; set; }
    public string KitchenStationCode { get; set; } = "";
    public string KitchenStationName { get; set; } = "";
    public bool StationActive { get; set; }
    public Guid UserId { get; set; }
    public KitchenEmployeeDTO? Employee { get; set; }
}
