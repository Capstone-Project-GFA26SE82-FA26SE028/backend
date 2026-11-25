using System.Linq.Expressions;
using Billing.Common.DTOs;
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

    public async Task<IReadOnlyList<PaymentSubmissionDTO>> GetIncomingCandidatesAsync(Guid restaurantId,
        Guid cashRegisterId, CancellationToken ct)
    {
        var unpaid = db.Bills.AsNoTracking().Where(bill => bill.RestaurantId == restaurantId
            && bill.Status == "Unpaid" && bill.PaidAt == null
            && !bill.Payments.Any(payment => payment.Status == "Paid" || payment.PaidAt != null));
        var active = db.BillCheckouts.AsNoTracking().Where(checkout => checkout.ReleasedAt == null
            && checkout.Status != "Paid" && checkout.Status != "ReleasePending");
        var routed = active.Where(checkout => checkout.CashRegisterId == cashRegisterId
            && checkout.SubmittedAt != null && checkout.BillId == null)
            .Select(checkout => new
            {
                CheckoutId = (Guid?)checkout.CheckoutId,
                TableSessionId = checkout.TableSessionRef,
                BillId = (Guid?)null,
                checkout.SubmittedAt,
                checkout.Status,
                BillCode = (string?)null,
                TotalAmount = (decimal?)null
            });

        // Older bills have no queue metadata. The bill's restaurant and single table session
        // determine which register can pick it up. Historical payment/shift records stay intact.
        var unpaidSessions = (from line in db.BillLines
            join bill in unpaid on line.BillId equals bill.BillId
            where !bill.BillLines.Any(other => other.TableSessionRef != line.TableSessionRef)
            select new { bill.BillId, TableSessionId = line.TableSessionRef, bill.BillNumber, bill.TotalAmount }).Distinct();
        var bills = from bill in unpaidSessions
            from checkout in active.Where(value => value.TableSessionRef == bill.TableSessionId
                && (value.BillId == bill.BillId || (value.BillId == null && value.CashRegisterId == cashRegisterId))
                && (value.CashRegisterId == null || value.CashRegisterId == cashRegisterId)).DefaultIfEmpty()
            select new
            {
                CheckoutId = checkout == null ? (Guid?)null : checkout.CheckoutId,
                bill.TableSessionId,
                BillId = (Guid?)bill.BillId,
                SubmittedAt = checkout == null ? (DateTime?)null : checkout.SubmittedAt,
                Status = checkout == null ? "Unpaid" : checkout.Status,
                BillCode = (string?)bill.BillNumber,
                TotalAmount = (decimal?)bill.TotalAmount
            };

        var queue = bills.Concat(routed.Where(row => !bills.Any(bill => bill.CheckoutId == row.CheckoutId)));
        var rows = await queue.OrderBy(row => row.SubmittedAt).ThenBy(row => row.TableSessionId)
            .ThenBy(row => row.CheckoutId).ThenBy(row => row.BillId).ToListAsync(ct);
        return rows.Select(row => new PaymentSubmissionDTO(row.CheckoutId, row.TableSessionId, cashRegisterId,
            row.SubmittedAt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(row.SubmittedAt.Value, DateTimeKind.Utc)) : null,
            row.Status, row.BillId)
        {
            BillCode = row.BillCode,
            TotalAmount = row.TotalAmount
        }).ToArray();
    }

    public async Task<IReadOnlyList<PaymentSubmissionDTO>> GetPaidCandidatesAsync(Guid restaurantId,
        Guid cashRegisterId, Guid cashShiftId, CancellationToken ct)
    {
        var bills = from bill in db.Bills.AsNoTracking()
            where bill.RestaurantId == restaurantId && bill.Status == "Paid" && bill.PaidAt != null
                && bill.Payments.Any(payment => payment.Status == "Paid" && payment.PaidAt != null
                    && payment.CashShiftId == cashShiftId)
            from line in bill.BillLines
            where !bill.BillLines.Any(other => other.TableSessionRef != line.TableSessionRef)
            select new { bill.BillId, bill.BillNumber, bill.TotalAmount, bill.PaidAt, TableSessionId = line.TableSessionRef };
        var query = from bill in bills.Distinct()
            from checkout in db.BillCheckouts.Where(value => value.BillId == bill.BillId
                && value.TableSessionRef == bill.TableSessionId && value.Status == "Paid")
                .OrderByDescending(value => value.CreatedAt).ThenBy(value => value.CheckoutId).Take(1).DefaultIfEmpty()
            orderby bill.PaidAt descending, bill.BillId
            select new
            {
                bill.BillId,
                bill.BillNumber,
                bill.TotalAmount,
                bill.PaidAt,
                bill.TableSessionId,
                CheckoutId = checkout == null ? (Guid?)null : checkout.CheckoutId,
                SubmittedAt = checkout == null ? (DateTime?)null : checkout.SubmittedAt
            };
        var rows = await query.ToListAsync(ct);
        return rows.Select(row => new PaymentSubmissionDTO(row.CheckoutId, row.TableSessionId, cashRegisterId,
            row.SubmittedAt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(row.SubmittedAt.Value, DateTimeKind.Utc)) : null,
            "Paid", row.BillId)
        {
            BillCode = row.BillNumber,
            TotalAmount = row.TotalAmount,
            PaidAt = new DateTimeOffset(DateTime.SpecifyKind(row.PaidAt!.Value, DateTimeKind.Utc))
        }).ToArray();
    }

    public void Add(BillCheckout value)
    {
        db.BillCheckouts.Add(value);
    }
}
