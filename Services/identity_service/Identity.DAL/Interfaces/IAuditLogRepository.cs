using Identity.Domain.Models;

namespace Identity.DAL.Interfaces;

public interface IAuditLogRepository
{
    Task<(List<AuditLog> Items, int Total)> GetAllAsync(int page, int pageSize, Guid? userId, CancellationToken ct);
}
