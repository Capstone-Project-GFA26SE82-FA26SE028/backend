using Billing.Common.DTOs;
using Shared.Common.Pagination;

namespace Billing.BLL.Interfaces;

public interface IBillService
{
    Task<BillDTO> Create(Guid tableSessionId, CancellationToken ct);
    Task<PagedResult<BillDTO>> GetAll(BillQueryDTO query, CancellationToken ct);
    Task<BillDTO> GetById(Guid id, CancellationToken ct);
}
