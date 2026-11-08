using System.Linq.Expressions;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Billing.DAL.Implements;

public sealed class BillCheckoutRepository(BillingDbContext db) : IBillCheckoutRepository
{
    public Task<BillCheckout?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        foreach (var entry in db.ChangeTracker.Entries<BillCheckout>().Where(entry => entry.Entity.CheckoutId == id).ToArray())
        {
            entry.State = EntityState.Detached;
        }

        return db.BillCheckouts.SingleOrDefaultAsync(value => value.CheckoutId == id, ct);
    }

    public Task<BillCheckout?> GetActiveAsync(Guid sessionId, CancellationToken ct)
    {
        return db.BillCheckouts.AsNoTracking().SingleOrDefaultAsync(value => value.TableSessionRef == sessionId && value.ReleasedAt == null, ct);
    }

    public Task<Guid?> GetBillIdAsync(Guid sessionId, CancellationToken ct)
    {
        return db.BillLines.Where(line => line.TableSessionRef == sessionId)
            .Select(line => (Guid?)line.BillId).Distinct().SingleOrDefaultAsync(ct);
    }

    public async Task<bool> HasPaymentOrFinalBillAsync(Guid sessionId, CancellationToken ct, Expression<Func<Payment, bool>>? blocking = null)
    {
        return await db.Bills.AnyAsync(bill => bill.BillLines.Any(line => line.TableSessionRef == sessionId)
            && (bill.Status != "Unpaid" || bill.PaidAt.HasValue), ct)
            || await db.Payments.Where(blocking ?? (value => true))
                .AnyAsync(value => value.Bill.BillLines.Any(line => line.TableSessionRef == sessionId), ct);
    }

    public void Add(BillCheckout value)
    {
        db.BillCheckouts.Add(value);
    }
}
