namespace Identity.Common.DTOs;

public sealed class RegisterCustomerDTO
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string FullName { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
}
