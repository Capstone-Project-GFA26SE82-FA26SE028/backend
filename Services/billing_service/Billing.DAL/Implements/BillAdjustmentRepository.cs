using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Persistence.Repositories;

namespace Billing.DAL.Implements;

public class BillAdjustmentRepository : GenericRepository<BillAdjustment, BillingDbContext>, IBillAdjustmentRepository
{
    public BillAdjustmentRepository(BillingDbContext context) : base(context)
    {
    }

    public async Task<List<BillAdjustment>> GetByBillIdAsync(Guid billId, CancellationToken ct)
    {
        return await _context.BillAdjustments
            .AsNoTracking()
            .Where(a => a.BillId == billId)
            .ToListAsync(ct);
    }

    public async Task AddAsync(BillAdjustment adjustment, CancellationToken ct)
    {
        await _context.BillAdjustments.AddAsync(adjustment, ct);
    }

    public async Task RemoveByBillAndVoucherAsync(Guid billId, Guid voucherId, CancellationToken ct)
    {
        var adjustments = await _context.BillAdjustments
            .Where(a => a.BillId == billId && a.VoucherId == voucherId)
            .ToListAsync(ct);

        _context.BillAdjustments.RemoveRange(adjustments);
    }

    public async Task RemoveByBillIdAsync(Guid billId, CancellationToken ct)
    {
        var adjustments = await _context.BillAdjustments
            .Where(a => a.BillId == billId)
            .ToListAsync(ct);

        _context.BillAdjustments.RemoveRange(adjustments);
    }
}
