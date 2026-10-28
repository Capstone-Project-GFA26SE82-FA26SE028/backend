using Billing.Common.DTOs;

namespace Billing.BLL.Interfaces;

public interface ICashMovementService
{
    Task<(CashMovementDTO Movement, bool Created)> Create(Guid shiftId, string key, CreateCashMovementDTO request, CancellationToken ct);
    Task<CashLedgerDTO> GetAll(Guid shiftId, int page, int pageSize, CancellationToken ct);
    Task<CashMovementDTO> GetById(Guid id, CancellationToken ct);
}
