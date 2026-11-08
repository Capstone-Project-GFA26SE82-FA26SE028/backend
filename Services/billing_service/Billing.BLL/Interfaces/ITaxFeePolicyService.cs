using Billing.Common.DTOs;
using Shared.Common.Pagination;

namespace Billing.BLL.Interfaces;

public interface ITaxFeePolicyService
{
    Task<TaxFeePolicyDTO> Create(CreateTaxFeePolicyDTO request, CancellationToken ct);

    Task<PagedResult<TaxFeePolicyDTO>> GetAll(TaxFeePolicyQueryDTO query, CancellationToken ct);

    Task<TaxFeePolicyDTO> GetById(Guid id, CancellationToken ct);
}
