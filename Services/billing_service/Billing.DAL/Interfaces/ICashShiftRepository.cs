using Billing.DAL.Models;

namespace Billing.DAL.Interfaces;

public interface ICashShiftRepository
{
    Task<CashShift?> GetDetailsAsync(Guid id, CancellationToken ct);
    Task<CashShift?> LockByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<CashShift>> GetUnclosedAsync(Guid cashRegisterId, CancellationToken cancellationToken);
    Task<CashShift> AddAsync(CashShift shift);
}
