using Identity.Common.DTOs;
using Identity.DAL.Implements;
using Identity.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Identity.Tests;

public sealed partial class UserManagementTests
{
    [Fact]
    public async Task NewLoginRevokesPreviousSessionAndRefreshTokenOnlyForSameUser()
    {
        var auth = await PrepareAuth();
        var otherSession = new UserSession
        {
            UserSessionId = Guid.NewGuid(),
            UserId = adminId,
            LoggedInAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            RefreshTokenHash = new string('d', 64)
        };
        db.UserSessions.Add(otherSession);
        await db.SaveChangesAsync();
        var request = new AuthDTO.LoginDTO
        {
            Email = "member@example.test",
            Password = "CurrentPassword123!"
        };
        var first = Assert.IsType<AuthDTO.LoginResponseDTO>((await auth.Login(request)).Result);
        var second = Assert.IsType<AuthDTO.LoginResponseDTO>((await auth.Login(request)).Result);
        var repository = new SessionRepository(db);

        Assert.False(await repository.IsActiveAsync(first.SessionId, memberId, "member", DateTime.UtcNow));
        Assert.True(await repository.IsActiveAsync(second.SessionId, memberId, "member", DateTime.UtcNow));
        Assert.True(await repository.IsActiveAsync(otherSession.UserSessionId, adminId, "admin", DateTime.UtcNow));
        Assert.Equal(1, await db.UserSessions.CountAsync(item => item.UserId == memberId && item.RevokedAt == null));
        Assert.Equal(401, (await auth.Refresh(new AuthDTO.RefreshDTO
        {
            RefreshToken = first.RefreshToken
        })).StatusCode);
        Assert.Equal(200, (await auth.Refresh(new AuthDTO.RefreshDTO
        {
            RefreshToken = second.RefreshToken
        })).StatusCode);
    }

    [Fact]
    public async Task FailedLoginPreservesExistingSession()
    {
        var auth = await PrepareAuth();
        var response = await auth.Login(new AuthDTO.LoginDTO
        {
            Email = "member@example.test",
            Password = "WrongPassword123!"
        });

        Assert.Equal(401, response.StatusCode);
        Assert.Null((await db.UserSessions.AsNoTracking().SingleAsync()).RevokedAt);
    }

    [Fact]
    public async Task FailedLoginAuditRollsBackSessionReplacement()
    {
        var auth = await PrepareAuth();
        var original = await db.UserSessions.AsNoTracking().SingleAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_login_audit BEFORE INSERT ON AuditLog
            BEGIN SELECT RAISE(ABORT, 'Audit unavailable'); END;
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() => auth.Login(new AuthDTO.LoginDTO
        {
            Email = "member@example.test",
            Password = "CurrentPassword123!"
        }));

        var remaining = await db.UserSessions.AsNoTracking().SingleAsync();
        Assert.Equal(original.UserSessionId, remaining.UserSessionId);
        Assert.Null(remaining.RevokedAt);
    }
}
