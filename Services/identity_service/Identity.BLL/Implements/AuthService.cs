using Shared.Common.DTOs;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Identity.BLL.Interfaces;
using Identity.Common.DTOs;
using Identity.Common.Settings;
using Identity.DAL.Interfaces;
using Identity.DAL.UnitOfWorks;
using Identity.Domain.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using static Identity.Common.DTOs.AuthDTO;

namespace Identity.BLL.Implements;

public sealed class AuthService(IUnitOfWork unitOfWork, ISessionRepository sessions,
    IOptions<JwtSettingModel> options) : IAuthService
{
    private JwtSettingModel Settings => options.Value;
    private static DateTime UtcNow()
    {
        return DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()).UtcDateTime;
    }

    private static string NewRefreshToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }

    private static string Hash(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    public async Task<ResponseDTO> Login(LoginDTO loginDTO)
    {
        if (loginDTO is null || string.IsNullOrWhiteSpace(loginDTO.Email)
            || loginDTO.Email.Trim().Length > 255 || string.IsNullOrEmpty(loginDTO.Password)
            || Encoding.UTF8.GetByteCount(loginDTO.Password) > 72)
        {
            return new ResponseDTO("Invalid email or password.", 400);
        }

        var user = await unitOfWork.UserRepo.FindByEmailAsync(loginDTO.Email.Trim());
        if (user is null || !VerifyPassword(loginDTO.Password, user.PasswordHash))
        {
            unitOfWork.RecordAudit(user?.UserId, "LOGIN_FAILED", "User", user?.UserId.ToString());
            await unitOfWork.SaveAsync();
            return new ResponseDTO("Incorrect email or password.", 401);
        }

        if (!user.IsActive)
        {
            unitOfWork.RecordAudit(user.UserId, "LOGIN_BLOCKED", "User", user.UserId.ToString());
            await unitOfWork.SaveAsync();
            return new ResponseDTO("The account is inactive.", 403);
        }

        var now = UtcNow();
        var refreshToken = NewRefreshToken();
        var session = new UserSession
        {
            UserSessionId = Guid.NewGuid(),
            UserId = user.UserId,
            LoggedInAt = now,
            IpAddress = System.Net.IPAddress.TryParse(loginDTO.IpAddress, out var address)
                ? address.ToString() : null!,
            UserAgent = string.IsNullOrWhiteSpace(loginDTO.UserAgent) ? null! : loginDTO.UserAgent.Trim(),
            DeviceType = DetectDeviceType(loginDTO.UserAgent),
            ExpiresAt = now.AddHours(Settings.SessionHours),
            RefreshTokenHash = Hash(refreshToken)
        };
        var result = CreateResponse(user, session, refreshToken, now);
        if (!await sessions.CreateExclusiveAsync(session, user.PasswordHash, user.RoleId))
        {
            return new ResponseDTO("Account information has changed. Please sign in again.", 401);
        }

        return new ResponseDTO("Signed in successfully.", 200, true, result);
    }

    public async Task<ResponseDTO> Refresh(RefreshDTO request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RefreshToken)
            || request.RefreshToken.Length > 512)
        {
            return new ResponseDTO("Invalid refresh token.", 400);
        }

        var hash = Hash(request.RefreshToken);
        var session = await sessions.FindByHashAsync(hash);
        var now = UtcNow();
        if (session is null || session.RevokedAt != null || session.ExpiresAt <= now || !session.User.IsActive)
        {
            return new ResponseDTO("The session has expired or is invalid. Please sign in again.", 401);
        }

        var refreshToken = NewRefreshToken();
        var result = CreateResponse(session.User, session, refreshToken, now);
        // Compare-and-swap prevents concurrent requests from reusing the same token.
        if (!await sessions.RotateAsync(session.UserSessionId, hash, Hash(refreshToken), now))
        {
            return new ResponseDTO("The refresh token has already been used or the session has been revoked.", 401);
        }

        return new ResponseDTO("Token refreshed successfully.", 200, true, result);
    }

    public async Task<ResponseDTO> Logout(Guid sessionId, Guid userId)
    {
        await sessions.RevokeAsync(sessionId, userId, UtcNow());
        return new ResponseDTO("Signed out successfully.", 200, true);
    }

    public async Task<ResponseDTO> ChangePassword(Guid userId, ChangePasswordDTO request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.CurrentPassword)
            || Encoding.UTF8.GetByteCount(request.CurrentPassword) > 72
            || string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8
            || Encoding.UTF8.GetByteCount(request.NewPassword) > 72
            || request.NewPassword != request.ConfirmPassword
            || request.CurrentPassword == request.NewPassword)
        {
            return new ResponseDTO("The new password must contain at least 8 characters and at most 72 UTF-8 bytes, differ from the current password, and match the confirmation.", 400);
        }

        var user = await unitOfWork.UserRepo.FindUserAsync(userId, ct);
        if (user is null || !user.IsActive)
        {
            return new ResponseDTO("Invalid account.", 401);
        }

        if (!VerifyPassword(request.CurrentPassword, user.PasswordHash))
        {
            unitOfWork.RecordAudit(userId, "CHANGE_PASSWORD_FAILED", "User", userId.ToString());
            await unitOfWork.SaveAsync(ct);
            return new ResponseDTO("The current password is incorrect.", 400);
        }

        var hash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        var changed = await unitOfWork.ExecuteInTransactionAsync(
            () => unitOfWork.UserRepo.ChangePasswordAsync(userId, user.PasswordHash, hash, ct), ct);
        if (!changed)
        {
            return new ResponseDTO("The account has changed. Please sign in again.", 409);
        }

        return new ResponseDTO("Password changed successfully. Please sign in again.", 200, true);
    }

    private static string DetectDeviceType(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return "Unknown";
        }

        // User-Agent is a hint, not a verified device identity.
        if (userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("Tablet", StringComparison.OrdinalIgnoreCase)
            || (userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase)
                && !userAgent.Contains("Mobile", StringComparison.OrdinalIgnoreCase)))
        {
            return "Tablet";
        }

        if (userAgent.Contains("Mobile", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase))
        {
            return "Mobile";
        }

        if (userAgent.Contains("Windows NT", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("Macintosh", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("X11", StringComparison.OrdinalIgnoreCase))
        {
            return "Desktop";
        }

        return "Unknown";
    }

    private LoginResponseDTO CreateResponse(User user, UserSession session, string refreshToken, DateTime now)
    {
        // SQL datetime does not preserve DateTime.Kind; all persisted times are UTC.
        var sessionExpiresAt = DateTime.SpecifyKind(session.ExpiresAt, DateTimeKind.Utc);
        var expiresAt = now.AddMinutes(Settings.AccessTokenMinutes);
        if (expiresAt > sessionExpiresAt)
        {
            expiresAt = sessionExpiresAt;
        }

        var token = new JwtSecurityToken(
            issuer: Settings.Issuer, audience: Settings.Audience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("UserId", user.UserId.ToString()), new Claim("UserName", user.UserName),
                new Claim("Email", user.Email), new Claim("RoleId", user.RoleId),
                new Claim("sid", session.UserSessionId.ToString())
            },
            notBefore: now, expires: expiresAt,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Settings.SecretKey)), SecurityAlgorithms.HmacSha256));
        return new LoginResponseDTO
        {
            User = new UserDTO
            {
                UserId = user.UserId,
                UserName = user.UserName,
                Email = user.Email,
                FullName = user.FullName,
                PhoneNumber = user.PhoneNumber,
                RoleId = user.RoleId,
                AvatarUrl = user.AvatarUrl,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt
            },
            AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
            RefreshToken = refreshToken,
            SessionId = session.UserSessionId,
            ExpiresAt = expiresAt,
            SessionExpiresAt = sessionExpiresAt
        };
    }

    private static bool VerifyPassword(string password, string? hash)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException) { return false; }
        catch (ArgumentException) { return false; }
        catch (FormatException) { return false; }
    }
}
