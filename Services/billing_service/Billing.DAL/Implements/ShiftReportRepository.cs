using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Pagination;
using Shared.Persistence.Repositories;

namespace Billing.DAL.Implements;

public sealed class ShiftReportRepository(BillingDbContext db) : GenericRepository<ShiftReport, BillingDbContext>(db), IShiftReportRepository
{
    public Task<ShiftReport?> GetForShiftAsync(Guid shiftId, CancellationToken ct)
    {
        return Details().SingleOrDefaultAsync(value => value.CashShiftId == shiftId, ct);
    }

    public Task<ShiftReport?> GetDetailsAsync(Guid id, CancellationToken ct)
    {
        return Details().SingleOrDefaultAsync(value => value.ShiftReportId == id, ct);
    }

    public async Task<PagedResult<ShiftReport>> GetPageAsync(Guid? restaurantId, IReadOnlyList<Guid>? allowedRestaurants, int page, int pageSize, CancellationToken ct)
    {
        var query = Details();
        if (allowedRestaurants is not null)
        {
            query = query.Where(value => allowedRestaurants.Contains(value.CashShift.CashRegister.RestaurantId));
        }

        if (restaurantId.HasValue)
        {
            query = query.Where(value => value.CashShift.CashRegister.RestaurantId == restaurantId.Value);
        }

        var count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(value => value.ClosedAt).ThenByDescending(value => value.ShiftReportId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<ShiftReport>(items, count, page, pageSize);
    }

    private IQueryable<ShiftReport> Details()
    {
        return _context.ShiftReports.AsNoTracking().Include(value => value.CashShift).ThenInclude(shift => shift.CashRegister);
    }
}
