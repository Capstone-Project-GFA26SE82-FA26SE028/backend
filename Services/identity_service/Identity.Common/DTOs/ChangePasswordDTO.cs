namespace Identity.Common.DTOs;

public sealed class ChangePasswordDTO
{
    public string? CurrentPassword { get; set; }
    public string? NewPassword { get; set; }
    public string? ConfirmPassword { get; set; }
}
