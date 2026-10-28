using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Persistence.Repositories;

namespace Billing.DAL.Implements;

public sealed class CashMovementRepository(BillingDbContext db)
    : GenericRepository<CashMovement, BillingDbContext>(db), ICashMovementRepository
{
    public Task<bool> PaymentHasMovementAsync(Guid paymentId, CancellationToken ct)
    {
        return _context.CashMovements.AnyAsync(movement => movement.PaymentId == paymentId, ct);
    }
}
