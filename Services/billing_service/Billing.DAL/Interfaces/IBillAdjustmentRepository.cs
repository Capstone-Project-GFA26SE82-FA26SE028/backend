using Billing.DAL.Models;
using Shared.Persistence.Interfaces;

namespace Billing.DAL.Interfaces;

public interface IBillAdjustmentRepository : IGenericRepository<BillAdjustment>
{
    Task<List<BillAdjustment>> GetByBillIdAsync(Guid billId, CancellationToken ct);
    Task AddAsync(BillAdjustment adjustment, CancellationToken ct);
    Task RemoveByBillAndVoucherAsync(Guid billId, Guid voucherId, CancellationToken ct);
    Task RemoveByBillIdAsync(Guid billId, CancellationToken ct);
}
