using Billing.Common.DTOs;
using Shared.Common.Pagination;

namespace Billing.BLL.Interfaces;

public interface ICashHandoverService
{
    Task<(CashHandoverDTO Handover, bool Created)> Create(Guid billId, string key, CreateCashHandoverDTO request, CancellationToken ct);
    Task<CashHandoverDTO> GetById(Guid paymentId, CancellationToken ct);
    Task<PagedResult<CashHandoverDTO>> GetAll(Guid registerId, int page, int pageSize, CancellationToken ct);
    Task<CashHandoverDTO> Confirm(Guid paymentId, decimal? confirmedAmount, CancellationToken ct);
}
