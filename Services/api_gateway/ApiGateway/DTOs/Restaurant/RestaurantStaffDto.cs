namespace ApiGateway.DTOs.Restaurant;

public sealed class RestaurantStaffDto
{
    public string? FullName { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? RoleId { get; set; }
    public string? AvatarUrl { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid UserIdRef { get; set; }
    public bool IsActive { get; set; }
}
