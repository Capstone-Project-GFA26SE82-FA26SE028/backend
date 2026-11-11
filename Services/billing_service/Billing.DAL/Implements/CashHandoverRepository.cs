using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Pagination;

namespace Billing.DAL.Implements;

public sealed class CashHandoverRepository(BillingDbContext db) : ICashHandoverRepository
{
    public Task<CashHandover?> GetByIdAsync(Guid paymentId, CancellationToken ct)
    {
        return Details().SingleOrDefaultAsync(value => value.PaymentId == paymentId, ct);
    }

    public async Task<PagedResult<CashHandover>> GetPageAsync(Guid shiftId, int page, int pageSize, CancellationToken ct)
    {
        var query = Details().Where(value => value.Payment.CashShiftId == shiftId && value.Status == "Pending");
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(value => value.CollectedAt).ThenBy(value => value.PaymentId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<CashHandover>(items, total, page, pageSize);
    }

    public void Add(CashHandover value)
    {
        db.CashHandovers.Add(value);
    }

    private IQueryable<CashHandover> Details()
    {
        return db.CashHandovers.AsNoTracking().Include(value => value.Payment).ThenInclude(value => value.Bill)
            .Include(value => value.Payment).ThenInclude(value => value.CashShift).ThenInclude(value => value.CashRegister);
    }
}
