using Billing.DAL.Models;
using Shared.Persistence.Interfaces;

namespace Billing.DAL.Interfaces;

public interface IVoucherRedemptionRepository : IGenericRepository<VoucherRedemption>
{
    Task<VoucherRedemption?> GetByBillAndVoucherAsync(Guid billId, Guid voucherId, CancellationToken ct);
    Task<List<VoucherRedemption>> GetByBillIdAsync(Guid billId, CancellationToken ct);
    Task AddAsync(VoucherRedemption redemption, CancellationToken ct);
    Task UpdateAsync(VoucherRedemption redemption, CancellationToken ct);
    Task DeleteAsync(VoucherRedemption redemption, CancellationToken ct);
}
