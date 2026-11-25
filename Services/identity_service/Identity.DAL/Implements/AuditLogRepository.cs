using Identity.DAL.Interfaces;
using Identity.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Identity.DAL.Implements;

public sealed class AuditLogRepository(IdentityDbContext context) : IAuditLogRepository
{
    public async Task<(List<AuditLog> Items, int Total)> GetAllAsync(int page, int pageSize, Guid? userId, CancellationToken ct, Identity.Common.DTOs.AuditLogFilter? filter = null)
    {
        var query = context.AuditLogs.AsNoTracking();
        if (userId.HasValue)
        {
            query = query.Where(item => item.UserId == userId.Value);
        }

        var search = filter?.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(item => item.User != null && (item.User.FullName.Contains(search)
                || item.User.UserName.Contains(search) || item.User.Email.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(filter?.Action))
        {
            query = query.Where(item => item.Action == filter.Action.Trim());
        }

        if (filter?.FromUtc is { } from)
        {
            query = query.Where(item => item.CreatedAt >= from);
        }

        if (filter?.ToUtc is { } to)
        {
            query = query.Where(item => item.CreatedAt <= to);
        }

        if (filter?.RestaurantId.HasValue == true)
        {
            query = query.Where(item => item.User != null && item.User.RestaurantId == filter.RestaurantId.Value);
        }

        var total = await query.CountAsync(ct);
        var items = await query.Include(item => item.User)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.AuditLogId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }
}
