using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Persistence.Repositories;

namespace Billing.DAL.Implements;

public sealed class CashShiftRepository(BillingDbContext db)
    : GenericRepository<CashShift, BillingDbContext>(db), ICashShiftRepository
{
    public async Task<IReadOnlyList<CashShift>> GetUnclosedAsync(Guid cashRegisterId, CancellationToken cancellationToken)
    {
        return await _context.CashShifts.AsNoTracking()
            .Where(shift => shift.CashRegisterId == cashRegisterId && shift.ClosedAt == null)
            .OrderBy(shift => shift.OpenedAt).ThenBy(shift => shift.CashShiftId)
            .Take(2).ToListAsync(cancellationToken);
    }
}
