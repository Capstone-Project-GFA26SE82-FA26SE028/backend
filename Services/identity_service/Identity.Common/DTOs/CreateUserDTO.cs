namespace Identity.Common.DTOs;

public sealed class CreateUserDTO
{
    public string? UserName { get; set; }

    public string? Email { get; set; }

    public string? Password { get; set; }

    public string? FullName { get; set; }

    public string? PhoneNumber { get; set; }

    public string? RoleId { get; set; }
}
