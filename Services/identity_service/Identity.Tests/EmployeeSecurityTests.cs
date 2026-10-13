using Identity.BLL.Implements;
using Identity.Common.DTOs;
using Identity.Common.Settings;
using Identity.DAL.Implements;
using Identity.DAL.UnitOfWorks;
using Identity.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Common.Pagination;
using Xunit;

namespace Identity.Tests;

public sealed partial class UserManagementTests
{
    private async Task<AuthService> PrepareAuth()
    {
        var user = await db.Users.SingleAsync(item => item.UserId == memberId);
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword("CurrentPassword123!");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        db.AuditIpAddress = "127.0.0.1";
        return new AuthService(new UnitOfWork(db), new SessionRepository(db),
            Options.Create(new JwtSettingModel
            {
                SecretKey = new string('k', 64)
            }));
    }

    [Fact]
    public async Task ChangePasswordRevokesAllOwnSessionsAndNeverAuditsSecrets()
    {
        var auth = await PrepareAuth();
        var adminSession = new UserSession
        {
            UserSessionId = Guid.NewGuid(),
            UserId = adminId,
            RefreshTokenHash = new string('b', 64),
            LoggedInAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        db.UserSessions.Add(adminSession);
        await db.SaveChangesAsync();
        var result = await auth.ChangePassword(memberId, new ChangePasswordDTO
        {
            CurrentPassword = "CurrentPassword123!",
            NewPassword = "NewPassword456!",
            ConfirmPassword = "NewPassword456!"
        }, default);

        Assert.Equal(200, result.StatusCode);
        var user = await db.Users.AsNoTracking().SingleAsync(item => item.UserId == memberId);
        Assert.True(BCrypt.Net.BCrypt.Verify("NewPassword456!", user.PasswordHash));
        Assert.False(BCrypt.Net.BCrypt.Verify("CurrentPassword123!", user.PasswordHash));
        Assert.All(await db.UserSessions.AsNoTracking().Where(item => item.UserId == memberId).ToListAsync(),
            session => Assert.NotNull(session.RevokedAt));
        Assert.Null((await db.UserSessions.AsNoTracking().SingleAsync(item => item.UserId == adminId)).RevokedAt);
        var logs = await db.AuditLogs.AsNoTracking().ToListAsync();
        Assert.Contains(logs, item => item.Action == "CHANGE_PASSWORD" && item.UserId == memberId);
        Assert.Contains(logs, item => item.Action == "REVOKE_SESSION");
        var json = System.Text.Json.JsonSerializer.Serialize(logs);
        Assert.DoesNotContain("CurrentPassword123!", json);
        Assert.DoesNotContain("NewPassword456!", json);
        Assert.DoesNotContain(user.PasswordHash, json);
        Assert.All(logs, item => Assert.Equal("127.0.0.1", item.IpAddress));
    }

    [Theory]
    [InlineData("WrongPassword123!", "NewPassword456!", "NewPassword456!")]
    [InlineData("CurrentPassword123!", "NewPassword456!", "Mismatch456!")]
    [InlineData("CurrentPassword123!", "short", "short")]
    [InlineData("CurrentPassword123!", "CurrentPassword123!", "CurrentPassword123!")]
    [InlineData("", "NewPassword456!", "NewPassword456!")]
    public async Task InvalidPasswordChangePreservesPasswordAndSessions(string current, string next, string confirm)
    {
        var auth = await PrepareAuth();
        var result = await auth.ChangePassword(memberId, new ChangePasswordDTO
        {
            CurrentPassword = current,
            NewPassword = next,
            ConfirmPassword = confirm
        }, default);

        Assert.Equal(400, result.StatusCode);
        var user = await db.Users.AsNoTracking().SingleAsync(item => item.UserId == memberId);
        Assert.True(BCrypt.Net.BCrypt.Verify("CurrentPassword123!", user.PasswordHash));
        Assert.Null((await db.UserSessions.AsNoTracking().SingleAsync()).RevokedAt);
        Assert.False(await db.AuditLogs.AnyAsync(item => item.Action == "CHANGE_PASSWORD"));
    }

    [Fact]
    public async Task PasswordChangeRejectsMoreThan72Utf8Bytes()
    {
        var auth = await PrepareAuth();
        var password = new string('é', 40);
        var result = await auth.ChangePassword(memberId, new ChangePasswordDTO
        {
            CurrentPassword = "CurrentPassword123!",
            NewPassword = password,
            ConfirmPassword = password
        }, default);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task PersonalSessionsAreIsolatedAndRevocationIsIdempotent()
    {
        var ownSession = await db.UserSessions.AsNoTracking().SingleAsync();
        var page = await sessions.GetMe(memberId, 1, 20, default);
        Assert.Equal(memberId, Assert.Single(Assert.IsType<PagedResult<SessionDTO>>(page.Result).Items).UserId);
        var empty = await sessions.GetMe(adminId, 1, 20, default);
        Assert.Empty(Assert.IsType<PagedResult<SessionDTO>>(empty.Result).Items);
        Assert.Equal(400, (await sessions.GetMe(memberId, 0, 20, default)).StatusCode);
        Assert.Equal(404, (await sessions.Delete(adminId, ownSession.UserSessionId, default)).StatusCode);
        Assert.Null((await db.UserSessions.AsNoTracking().SingleAsync()).RevokedAt);
        Assert.Empty(await db.AuditLogs.ToListAsync());
        Assert.Equal(200, (await sessions.Delete(memberId, ownSession.UserSessionId, default)).StatusCode);
        Assert.Equal(200, (await sessions.Delete(memberId, ownSession.UserSessionId, default)).StatusCode);
        Assert.Single(await db.AuditLogs.Where(item => item.Action == "REVOKE_SESSION").ToListAsync());
        Assert.False(await new SessionRepository(db).IsActiveAsync(
            ownSession.UserSessionId, memberId, "member", DateTime.UtcNow));
    }

    [Fact]
    public async Task LogoutAllOnlyRevokesTheActorsSessions()
    {
        db.UserSessions.Add(new UserSession
        {
            UserSessionId = Guid.NewGuid(),
            UserId = adminId,
            RefreshTokenHash = new string('c', 64),
            LoggedInAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        });
        await db.SaveChangesAsync();
        Assert.Equal(200, (await sessions.DeleteAll(memberId, default)).StatusCode);
        Assert.NotNull((await db.UserSessions.AsNoTracking().SingleAsync(item => item.UserId == memberId)).RevokedAt);
        Assert.Null((await db.UserSessions.AsNoTracking().SingleAsync(item => item.UserId == adminId)).RevokedAt);
    }

    [Fact]
    public async Task RefreshAndLogoutProduceAuditsWithoutTokenValues()
    {
        var auth = await PrepareAuth();
        var response = await auth.Login(new AuthDTO.LoginDTO
        {
            Email = "member@example.test",
            Password = "CurrentPassword123!"
        });
        var login = Assert.IsType<AuthDTO.LoginResponseDTO>(response.Result);
        var refreshed = await auth.Refresh(new AuthDTO.RefreshDTO
        {
            RefreshToken = login.RefreshToken
        });
        Assert.Equal(200, refreshed.StatusCode);
        Assert.Equal(401, (await auth.Refresh(new AuthDTO.RefreshDTO
        {
            RefreshToken = login.RefreshToken
        })).StatusCode);
        await auth.Logout(login.SessionId, memberId);
        var current = Assert.IsType<AuthDTO.LoginResponseDTO>(refreshed.Result);
        Assert.Equal(401, (await auth.Refresh(new AuthDTO.RefreshDTO
        {
            RefreshToken = current.RefreshToken
        })).StatusCode);
        var logs = await db.AuditLogs.AsNoTracking().ToListAsync();
        Assert.Single(logs, item => item.Action == "REFRESH_TOKEN");
        Assert.Single(logs, item => item.Action == "LOGOUT");
        var json = System.Text.Json.JsonSerializer.Serialize(logs);
        Assert.DoesNotContain(login.RefreshToken, json);
        Assert.DoesNotContain(current.RefreshToken, json);
    }

    [Fact]
    public async Task UserMutationsRecordActorAndBeforeAfterValues()
    {
        db.AuditIpAddress = "::1";
        await service.Create(adminId, Employee(), default);
        await service.UpdateMe(memberId, new UpdateProfileDTO
        {
            FullName = "Changed Name"
        }, default);
        await service.SetRole(adminId, memberId, "admin", default);
        await service.SetStatus(adminId, memberId, false, default);
        var logs = await db.AuditLogs.AsNoTracking().ToListAsync();
        var created = Assert.Single(logs, item => item.Action == "CREATE_USER");
        Assert.Equal(adminId, created.UserId);
        Assert.DoesNotContain("Password", created.NewValue);
        var profile = Assert.Single(logs, item => item.Action == "UPDATE_PROFILE");
        Assert.Equal(memberId, profile.UserId);
        Assert.Contains("Original name", profile.OldValue);
        Assert.Contains("Changed Name", profile.NewValue);
        Assert.Contains(logs, item => item.Action == "SET_ROLE" && item.UserId == adminId);
        Assert.Contains(logs, item => item.Action == "SET_STATUS" && item.UserId == adminId);
        Assert.All(logs, item => Assert.Equal("::1", item.IpAddress));
    }

    [Fact]
    public async Task FailedAuditRollsBackPasswordChangeAndSessionRevocation()
    {
        var auth = await PrepareAuth();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_audit BEFORE INSERT ON AuditLog
            BEGIN SELECT RAISE(ABORT, 'Audit unavailable'); END;
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() => auth.ChangePassword(memberId, new ChangePasswordDTO
        {
            CurrentPassword = "CurrentPassword123!",
            NewPassword = "NewPassword456!",
            ConfirmPassword = "NewPassword456!"
        }, default));
        var user = await db.Users.AsNoTracking().SingleAsync(item => item.UserId == memberId);
        Assert.True(BCrypt.Net.BCrypt.Verify("CurrentPassword123!", user.PasswordHash));
        Assert.Null((await db.UserSessions.AsNoTracking().SingleAsync()).RevokedAt);
        Assert.Empty(await db.AuditLogs.AsNoTracking().ToListAsync());
    }
}
