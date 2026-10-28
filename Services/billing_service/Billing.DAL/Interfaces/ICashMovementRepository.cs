using Billing.DAL.Models;
using Shared.Persistence.Interfaces;

namespace Billing.DAL.Interfaces;

public interface ICashMovementRepository : IGenericRepository<CashMovement>
{
    Task<bool> PaymentHasMovementAsync(Guid paymentId, CancellationToken ct);
}
