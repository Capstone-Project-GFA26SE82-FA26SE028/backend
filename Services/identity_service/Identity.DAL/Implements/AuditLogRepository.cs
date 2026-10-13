using Identity.DAL.Interfaces;
using Identity.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Identity.DAL.Implements;

public sealed class AuditLogRepository(IdentityDbContext context) : IAuditLogRepository
{
    public async Task<(List<AuditLog> Items, int Total)> GetAllAsync(int page, int pageSize, Guid? userId, CancellationToken ct)
    {
        var query = context.AuditLogs.AsNoTracking();
        if (userId.HasValue)
        {
            query = query.Where(item => item.UserId == userId.Value);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.AuditLogId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }
}
