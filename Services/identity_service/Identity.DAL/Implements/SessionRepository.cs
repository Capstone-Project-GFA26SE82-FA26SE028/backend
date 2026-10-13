using Identity.DAL.Interfaces;
using Identity.Domain.Models;
using Microsoft.EntityFrameworkCore;
namespace Identity.DAL.Implements;

public sealed class SessionRepository(IdentityDbContext context) : ISessionRepository
{
    public Task<UserSession?> FindByHashAsync(string hash)
    {
        return context.UserSessions.AsNoTracking().Include(s => s.User)
            .FirstOrDefaultAsync(s => s.RefreshTokenHash == hash);
    }

    public async Task<bool> RotateAsync(Guid id, string oldHash, string newHash, DateTime now)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        var changed = await context.UserSessions
            .Where(s => s.UserSessionId == id && s.RefreshTokenHash == oldHash
                && s.RevokedAt == null && s.ExpiresAt > now && s.User.IsActive)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.RefreshTokenHash, newHash)) == 1;
        if (changed)
        {
            var userId = await context.UserSessions.Where(session => session.UserSessionId == id)
                .Select(session => session.UserId).SingleAsync();
            context.RecordAudit(userId, "REFRESH_TOKEN", "UserSession", id.ToString());
            await context.SaveChangesAsync();
        }

        await transaction.CommitAsync();
        return changed;
    }

    public async Task RevokeAsync(Guid id, Guid userId, DateTime now)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        var changed = await context.UserSessions
            .Where(s => s.UserSessionId == id && s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.RevokedAt, (DateTime?)now));
        if (changed > 0)
        {
            context.RecordAudit(userId, "LOGOUT", "UserSession", id.ToString(),
                new { RevokedAt = (DateTime?)null }, new { RevokedAt = now });
            await context.SaveChangesAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task<bool> RevokeOwnAsync(Guid userId, Guid? sessionId, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var query = context.UserSessions.Where(session => session.UserId == userId);
        if (sessionId.HasValue)
        {
            query = query.Where(session => session.UserSessionId == sessionId.Value);
            if (!await query.AnyAsync(ct))
            {
                return false;
            }
        }

        var affected = await query.AsNoTracking().Where(session => session.RevokedAt == null).ToListAsync(ct);
        var now = DateTime.UtcNow;
        await query.Where(session => session.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(session => session.RevokedAt, (DateTime?)now), ct);
        foreach (var session in affected)
        {
            context.RecordAudit(userId, "REVOKE_SESSION", "UserSession", session.UserSessionId.ToString(),
                new { session.RevokedAt },
                new { RevokedAt = now, Reason = sessionId.HasValue ? "USER_REQUEST" : "LOGOUT_ALL" });
        }

        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public Task<bool> IsActiveAsync(Guid id, Guid userId, string roleId, DateTime now)
    {
        return context.UserSessions.AsNoTracking().AnyAsync(s =>
            s.UserSessionId == id && s.UserId == userId && s.RevokedAt == null
            && s.ExpiresAt > now && s.User.IsActive && s.User.RoleId == roleId);
    }

    public async Task<(List<UserSession> Items, int Total)> GetAllAsync(int page, int pageSize, Guid? userId, CancellationToken ct)
    {
        var query = context.UserSessions.AsNoTracking();
        if (userId.HasValue)
        {
            query = query.Where(item => item.UserId == userId.Value);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(item => item.LoggedInAt)
            .ThenByDescending(item => item.UserSessionId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }
}
