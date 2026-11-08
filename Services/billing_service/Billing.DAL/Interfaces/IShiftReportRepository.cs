using Billing.DAL.Models;
using Shared.Common.Pagination;
using Shared.Persistence.Interfaces;

namespace Billing.DAL.Interfaces;

public interface IShiftReportRepository : IGenericRepository<ShiftReport>
{
    Task<ShiftReport?> GetForShiftAsync(Guid shiftId, CancellationToken ct);
    Task<ShiftReport?> GetDetailsAsync(Guid id, CancellationToken ct);
    Task<PagedResult<ShiftReport>> GetPageAsync(Guid? restaurantId, IReadOnlyList<Guid>? allowedRestaurants, int page, int pageSize, CancellationToken ct);
}
