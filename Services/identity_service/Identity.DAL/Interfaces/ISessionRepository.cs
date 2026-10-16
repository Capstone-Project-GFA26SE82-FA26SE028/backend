using Identity.Domain.Models;
namespace Identity.DAL.Interfaces;

public interface ISessionRepository
{
    Task<bool> CreateExclusiveAsync(UserSession session, string passwordHash, string roleId);
    Task<bool> RevokeOwnAsync(Guid userId, Guid? sessionId, CancellationToken ct);
    Task<(List<UserSession> Items, int Total)> GetAllAsync(int page, int pageSize, Guid? userId, CancellationToken ct, Identity.Common.DTOs.SessionFilter? filter = null);
    Task<UserSession?> FindByHashAsync(string hash);
    Task<bool> RotateAsync(Guid id, string oldHash, string newHash, DateTime now);
    Task RevokeAsync(Guid id, Guid userId, DateTime now);
    Task<bool> IsActiveAsync(Guid id, Guid userId, string roleId, DateTime now);
}
