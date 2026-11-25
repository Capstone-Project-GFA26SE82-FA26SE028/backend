using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Persistence.Repositories;

namespace Billing.DAL.Implements;

public class VoucherRedemptionRepository : GenericRepository<VoucherRedemption, BillingDbContext>, IVoucherRedemptionRepository
{
    public VoucherRedemptionRepository(BillingDbContext context) : base(context)
    {
    }

    public async Task<VoucherRedemption?> GetByBillAndVoucherAsync(Guid billId, Guid voucherId, CancellationToken ct)
    {
        return await _context.VoucherRedemptions
            .Include(r => r.Voucher)
            .FirstOrDefaultAsync(r => r.BillId == billId && r.VoucherId == voucherId, ct);
    }

    public async Task<List<VoucherRedemption>> GetByBillIdAsync(Guid billId, CancellationToken ct)
    {
        return await _context.VoucherRedemptions
            .Include(r => r.Voucher)
            .Where(r => r.BillId == billId)
            .ToListAsync(ct);
    }

    public async Task AddAsync(VoucherRedemption redemption, CancellationToken ct)
    {
        await _context.VoucherRedemptions.AddAsync(redemption, ct);
    }

    public Task UpdateAsync(VoucherRedemption redemption, CancellationToken ct)
    {
        _context.VoucherRedemptions.Update(redemption);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(VoucherRedemption redemption, CancellationToken ct)
    {
        _context.VoucherRedemptions.Remove(redemption);
        return Task.CompletedTask;
    }
}
