using Billing.Common.DTOs;
using Shared.Common.Pagination;

namespace Billing.BLL.Interfaces;

public interface ICashRegisterService
{
    Task<CashRegisterDTO> Create(CreateCashRegisterDTO request, CancellationToken cancellationToken);
    Task<PagedResult<CashRegisterDTO>> GetAll(CashRegisterQueryDTO query, CancellationToken cancellationToken);
    Task<CashRegisterDTO> GetById(Guid id, CancellationToken cancellationToken);
}
