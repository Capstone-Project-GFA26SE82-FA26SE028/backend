using Billing.Common.DTOs;
using Billing.DAL.Models;
using Shared.Common.Pagination;
using Shared.Persistence.Interfaces;

namespace Billing.DAL.Interfaces;

public interface IVoucherRepository : IGenericRepository<Voucher>
{
    Task<Voucher?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Voucher?> FindByCodeAsync(string normalizedCode, CancellationToken ct);
    Task<PagedResult<Voucher>> GetAllAsync(VoucherQueryDTO query, CancellationToken ct);
    Task<List<Voucher>> GetActiveVouchersAsync(DateTime now, CancellationToken ct);
    Task<int> GetUsageCountAsync(Guid voucherId, CancellationToken ct);
    Task AddAsync(Voucher voucher, CancellationToken ct);
    Task UpdateAsync(Voucher voucher, CancellationToken ct);
}
