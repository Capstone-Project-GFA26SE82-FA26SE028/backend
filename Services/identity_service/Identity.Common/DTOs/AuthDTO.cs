namespace Identity.Common.DTOs;

public class AuthDTO
{
    public class LoginDTO
    {
        public string? Email { get; set; }
        public string? Password { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
    }

    public class RefreshDTO
    {
        public string? RefreshToken { get; set; }
    }

    public sealed class LoginResponseDTO
    {
        public required UserDTO User { get; init; }
        public required string AccessToken { get; init; }
        public required string RefreshToken { get; init; }
        public Guid SessionId { get; init; }
        public DateTime ExpiresAt { get; init; }
        public DateTime SessionExpiresAt { get; init; }
    }
}
