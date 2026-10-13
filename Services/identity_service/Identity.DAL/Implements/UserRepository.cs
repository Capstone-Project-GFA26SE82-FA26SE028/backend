using Shared.Persistence.Repositories;
using Identity.DAL.Interfaces;
using Identity.Domain.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Identity.DAL.Implements
{
    public class UserRepository : GenericRepository<User, IdentityDbContext>, IUserRepository
    {
        public UserRepository(IdentityDbContext context) : base(context)
        {
        }

        public Task<User?> FindUserAsync(Guid id, CancellationToken ct)
        {
            return _context.Users.FirstOrDefaultAsync(u => u.UserId == id, ct);
        }

        public async Task<bool> ChangePasswordAsync(Guid userId, string oldHash, string newHash, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var changed = await _context.Users
                .Where(user => user.UserId == userId && user.IsActive && user.PasswordHash == oldHash)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(user => user.PasswordHash, newHash)
                    .SetProperty(user => user.UpdatedAt, (DateTime?)now), ct);
            if (changed == 0)
            {
                return false;
            }

            var activeSessions = await _context.UserSessions.AsNoTracking()
                .Where(session => session.UserId == userId && session.RevokedAt == null)
                .ToListAsync(ct);
            await _context.UserSessions.Where(session => session.UserId == userId && session.RevokedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(session => session.RevokedAt, (DateTime?)now), ct);
            foreach (var session in activeSessions)
            {
                _context.RecordAudit(userId, "REVOKE_SESSION", "UserSession", session.UserSessionId.ToString(),
                    new { session.RevokedAt }, new { RevokedAt = now, Reason = "PASSWORD_CHANGED" });
            }

            _context.RecordAudit(userId, "CHANGE_PASSWORD", "User", userId.ToString());
            return true;
        }

        public async Task<(List<User> Items, int Total)> GetAllAsync(int page, int pageSize, string? search, CancellationToken ct)
        {
            var query = _context.Users.AsNoTracking();
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(u => u.Email.Contains(search) || u.UserName.Contains(search) || u.FullName.Contains(search));
            }

            var total = await query.CountAsync(ct);
            var items = await query.OrderBy(u => u.CreatedAt).ThenBy(u => u.UserId)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return (items, total);
        }


        public async Task<User?> FindByEmailAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return null;
            }

            var normalizedEmail = email.Trim().ToLower();
            return await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);
        }

        // Called inside UnitOfWork.ExecuteInTransactionAsync to protect concurrent changes.
        public async Task<(int Status, User? User)> CreateEmployeeAsync(Guid actorId, User user, string[] adminRoles, CancellationToken ct)
        {
            if (!await _context.Users.AnyAsync(u => u.UserId == actorId && u.IsActive && adminRoles.Contains(u.RoleId), ct))
            {
                return (403, null);
            }

            var assignedRole = await _context.Roles.Where(r => r.RoleId == user.RoleId).Select(r => r.RoleId).FirstOrDefaultAsync(ct);
            if (assignedRole is null)
            {
                return (400, null);
            }

            user.RoleId = assignedRole;
            var email = user.Email.ToLowerInvariant();
            var name = user.UserName.ToLowerInvariant();
            if (await _context.Users.AnyAsync(u => u.Email.ToLower() == email || u.UserName.ToLower() == name, ct))
            {
                return (409, null);
            }

            _context.Users.Add(user);
            _context.RecordAudit(actorId, "CREATE_USER", "User", user.UserId.ToString(), null,
                new
                {
                    user.UserName,
                    user.Email,
                    user.FullName,
                    user.PhoneNumber,
                    user.RoleId,
                    user.IsActive
                });
            return (201, user);
        }

        public async Task<(int Status, User? User)> ChangeAccessAsync(Guid actorId, Guid id, string? roleId, bool? isActive, string[] adminRoles, CancellationToken ct)
        {
            if (!await _context.Users.AnyAsync(u => u.UserId == actorId && u.IsActive && adminRoles.Contains(u.RoleId), ct))
            {
                return (403, null);
            }

            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == id, ct);
            if (user is null)
            {
                return (404, null);
            }

            var nextRole = roleId is null ? user.RoleId : await _context.Roles.Where(r => r.RoleId == roleId).Select(r => r.RoleId).FirstOrDefaultAsync(ct);
            if (nextRole is null)
            {
                return (400, null);
            }

            var nextActive = isActive ?? user.IsActive;
            if (user.IsActive && adminRoles.Contains(user.RoleId) && (!nextActive || !adminRoles.Contains(nextRole))
                && !await _context.Users.AnyAsync(u => u.UserId != id && u.IsActive && adminRoles.Contains(u.RoleId), ct))
            {
                return (409, null);
            }

            var now = DateTime.UtcNow;
            await _context.Users.Where(u => u.UserId == id).ExecuteUpdateAsync(set => set
                .SetProperty(u => u.RoleId, nextRole).SetProperty(u => u.IsActive, nextActive)
                .SetProperty(u => u.UpdatedAt, (DateTime?)now), ct);
            if (!nextActive || nextRole != user.RoleId)
            {
                var revokedSessions = await _context.UserSessions.AsNoTracking()
                    .Where(session => session.UserId == id && session.RevokedAt == null).ToListAsync(ct);
                await _context.UserSessions.Where(s => s.UserId == id && s.RevokedAt == null)
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.RevokedAt, (DateTime?)now), ct);
                foreach (var session in revokedSessions)
                {
                    _context.RecordAudit(actorId, "REVOKE_SESSION", "UserSession", session.UserSessionId.ToString(),
                        new { session.RevokedAt },
                        new { RevokedAt = now, Reason = "ACCESS_CHANGED" });
                }
            }

            _context.RecordAudit(actorId, roleId is null ? "SET_STATUS" : "SET_ROLE", "User", id.ToString(),
                new { user.RoleId, user.IsActive },
                new { RoleId = nextRole, IsActive = nextActive });
            user.RoleId = nextRole;
            user.IsActive = nextActive;
            user.UpdatedAt = now;
            return (200, user);
        }
    }
}
