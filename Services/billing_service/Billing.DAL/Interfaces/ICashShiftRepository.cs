using Billing.DAL.Models;

namespace Billing.DAL.Interfaces;

public interface ICashShiftRepository
{
    Task<IReadOnlyList<CashShift>> GetUnclosedAsync(Guid cashRegisterId, CancellationToken cancellationToken);
    Task<CashShift> AddAsync(CashShift shift);
}
