using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;
using Restaurant.Common.Settings;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;

namespace Restaurant.BLL.Implements;

public sealed class GuestAccessService(ITableSessionRepository sessions, IGuestAccessRepository guests,
    IManagementTransaction transaction, IOptions<GuestAccessSettings> settings) : IGuestAccessService
{
    public Task<GuestAccessDto> Join(Guid sessionId, string phoneOrCode, CancellationToken ct)
    {
        var input = phoneOrCode.Trim();
        if ((input.Length != 3 && (input.Length != 10 || input[0] != '0'))
            || input.Any(c => c < '0' || c > '9'))
        {
            throw new ManagementException(400, "Nhập mã gồm 3 chữ số hoặc số điện thoại gồm 10 chữ số bắt đầu bằng 0.");
        }

        return transaction.ExecuteAsync(async () =>
        {
            var session = await sessions.LockAsync(sessionId, ct)
                ?? throw new ManagementException(404, "Không tìm thấy phiên bàn.");
            if (session.ClosedAt != null || session.Status != "Open")
            {
                throw new ManagementException(409, "Phiên bàn đã đóng hoặc không còn nhận khách.");
            }

            var code = input[^3..];
            var now = DateTime.UtcNow;
            if (string.IsNullOrEmpty(session.JoinCodeHash))
            {
                var salt = RandomNumberGenerator.GetBytes(16);
                session.JoinCodeHash = Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(HashCode(code, salt));
                session.JoinCodeSetAt = now;
            }
            else
            {
                if (input.Length != 3)
                {
                    throw new ManagementException(400, "Phiên bàn đã có mã. Nhập đúng 3 chữ số để tham gia.");
                }

                var parts = session.JoinCodeHash.Split(':');
                if (!CryptographicOperations.FixedTimeEquals(
                    HashCode(code, Convert.FromBase64String(parts[0])), Convert.FromBase64String(parts[1])))
                {
                    throw new ManagementException(403, "Mã tham gia bàn không chính xác.");
                }
            }

            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var expiresAt = now.AddHours(settings.Value.LifetimeHours);
            var guestId = Guid.NewGuid();
            guests.Add(new GuestAccess
            {
                GuestAccessId = guestId,
                TableSessionId = sessionId,
                TokenHash = HashToken(token),
                CreatedAt = now,
                ExpiredAt = expiresAt
            });
            return new GuestAccessDto
            {
                GuestAccessId = guestId,
                TableSessionId = sessionId,
                ExpiresAt = expiresAt,
                Token = token
            };
        }, ct);
    }

    public async Task<GuestAccessDto> GetMe(Guid sessionId, string token, CancellationToken ct)
    {
        if (token.Length != 64 || !token.All(Uri.IsHexDigit))
        {
            throw new ManagementException(401, "Nhập mã để tham gia bàn.");
        }

        var access = await guests.FindByTokenHashAsync(HashToken(token), ct);
        if (access is null || access.TableSessionId != sessionId || access.RevokedAt != null
            || access.ExpiredAt <= DateTime.UtcNow || access.TableSession.ClosedAt != null
            || access.TableSession.Status != "Open")
        {
            throw new ManagementException(401, "Quyền truy cập phiên bàn không còn hợp lệ.");
        }

        return new GuestAccessDto
        {
            GuestAccessId = access.GuestAccessId,
            TableSessionId = sessionId,
            ExpiresAt = DateTime.SpecifyKind(access.ExpiredAt, DateTimeKind.Utc)
        };
    }

    private static byte[] HashCode(string code, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(code, salt, 100000, HashAlgorithmName.SHA256, 32);
    }

    private static string HashToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
