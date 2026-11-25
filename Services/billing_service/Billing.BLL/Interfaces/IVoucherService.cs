using Billing.Common.DTOs;
using Shared.Common.Pagination;

namespace Billing.BLL.Interfaces;

public interface IVoucherService
{
    Task<VoucherDTO> Create(CreateVoucherDTO dto, CancellationToken ct);
    Task<VoucherDTO> Update(Guid id, UpdateVoucherDTO dto, CancellationToken ct);
    Task<PagedResult<VoucherDTO>> GetAll(VoucherQueryDTO query, CancellationToken ct);
    Task<VoucherDTO> GetById(Guid id, CancellationToken ct);
    Task<VoucherDTO> SetStatus(Guid id, bool isActive, CancellationToken ct);
    Task<BillDTO> ApplyVoucher(Guid billId, string voucherCode, CancellationToken ct);
    Task<BillDTO> RemoveVoucher(Guid billId, Guid voucherId, CancellationToken ct);
}
