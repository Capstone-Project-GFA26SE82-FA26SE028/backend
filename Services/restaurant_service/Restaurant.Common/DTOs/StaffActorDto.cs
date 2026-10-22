namespace Restaurant.Common.DTOs;

public sealed class StaffActorDto
{
    public Guid UserId { get; set; }
    public string RoleId { get; set; } = string.Empty;
    public bool IsAdministrator { get; set; }
}
