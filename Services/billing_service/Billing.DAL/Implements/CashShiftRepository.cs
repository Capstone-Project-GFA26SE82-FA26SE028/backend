using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Persistence.Repositories;

namespace Billing.DAL.Implements;

public sealed class CashShiftRepository(BillingDbContext db)
    : GenericRepository<CashShift, BillingDbContext>(db), ICashShiftRepository
{
    public Task<CashShift?> GetDetailsAsync(Guid id, CancellationToken ct)
    {
        return _context.CashShifts.AsNoTracking().Include(shift => shift.CashRegister)
            .SingleOrDefaultAsync(shift => shift.CashShiftId == id, ct);
    }

    public Task<CashShift?> LockByIdAsync(Guid id, CancellationToken ct)
    {
        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("An active transaction is required to lock a cash shift.");
        }

        var source = _context.Database.IsSqlServer()
            ? _context.CashShifts.FromSqlInterpolated($"SELECT * FROM dbo.CashShift WITH (UPDLOCK, HOLDLOCK) WHERE CashShiftId={id}")
            : _context.CashShifts.Where(shift => shift.CashShiftId == id);
        return source.Include(shift => shift.CashRegister).SingleOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<CashShift>> GetUnclosedAsync(Guid cashRegisterId, CancellationToken cancellationToken)
    {
        return await _context.CashShifts.AsNoTracking()
            .Where(shift => shift.CashRegisterId == cashRegisterId && shift.ClosedAt == null)
            .OrderBy(shift => shift.OpenedAt).ThenBy(shift => shift.CashShiftId)
            .Take(2).ToListAsync(cancellationToken);
    }
}
