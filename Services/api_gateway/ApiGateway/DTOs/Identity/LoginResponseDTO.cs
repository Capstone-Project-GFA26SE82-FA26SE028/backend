namespace ApiGateway.DTOs.Identity;

public class LoginResponseDTO
{
    public UserDTO? User { get; set; }

    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }

    public DateTime? SessionExpiresAt { get; set; }
}
