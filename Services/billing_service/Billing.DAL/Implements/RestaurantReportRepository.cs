using System.Data;
using Billing.Common.Constants;
using Billing.Common.DTOs;
using Billing.DAL.DTOs;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Billing.DAL.Implements;

public sealed class RestaurantReportRepository(BillingDbContext db) : IRestaurantReportRepository
{
    public async Task<RestaurantReportSource> GetSourceAsync(Guid restaurantId, DateTime fromUtc, DateTime toUtc, CancellationToken ct, SalesReportFilterDTO? filters = null)
    {
        // Keep all source reads consistent while settlements/refunds are being committed.
        // No status changes or provider calls belong in reporting.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        filters ??= new SalesReportFilterDTO();
        var paymentQuery = db.Payments.Where(value => value.Bill.RestaurantId == restaurantId);
        var onlineQuery = db.OnlinePayments.Where(value => value.RestaurantId == restaurantId);
        if (filters.Shift == "unassigned")
        {
            paymentQuery = paymentQuery.Where(value => false);
        }

        if ((filters.Shift != "all" && filters.Shift != "unassigned")
            || (filters.MethodCode != "all" && filters.MethodCode != PayOsStates.Method))
        {
            onlineQuery = onlineQuery.Where(value => false);
        }

        if (filters.MethodCode != "all")
        {
            paymentQuery = paymentQuery.Where(value => value.MethodCode == filters.MethodCode);
        }

        if (Guid.TryParse(filters.Shift, out var shiftId))
        {
            paymentQuery = paymentQuery.Where(value => value.CashShiftId == shiftId);
        }

        var payments = await paymentQuery.AsNoTrackingWithIdentityResolution()
            .Where(value => value.PaidAt >= fromUtc && value.PaidAt < toUtc)
            .Include(value => value.Bill).ThenInclude(bill => bill.BillLines)
            .Include(value => value.Bill).ThenInclude(bill => bill.Payments)
            .Include(value => value.CashHandover)
            .Include(value => value.CashMovements)
            .AsSplitQuery().ToListAsync(ct);
        var onlineSales = await onlineQuery.AsNoTracking()
            .Where(value => value.PaidAt >= fromUtc && value.PaidAt < toUtc)
            .ToListAsync(ct);
        var onlineRefunds = await onlineQuery.AsNoTracking()
            .Where(value => value.RefundedAt >= fromUtc && value.RefundedAt < toUtc)
            .ToListAsync(ct);
        var missingPaymentTimes = await paymentQuery.CountAsync(value => value.Status == "Paid" && value.PaidAt == null, ct);
        missingPaymentTimes += await onlineQuery.CountAsync(value => (value.Status == "Paid" || value.Status == "RefundPending" || value.Status == "Refunded")
            && value.PaidAt == null, ct);
        var missingRefundTimes = await onlineQuery.CountAsync(value => value.Status == "Refunded" && value.RefundedAt == null, ct);
        var unverifiedDineInRefunds = await db.Refunds.CountAsync(value => paymentQuery.Select(payment => payment.PaymentId).Contains(value.PaymentId)
            && (value.CompletedAt == null || (value.CompletedAt >= fromUtc && value.CompletedAt < toUtc)), ct);
        await transaction.CommitAsync(ct);
        return new RestaurantReportSource(payments, onlineSales, onlineRefunds,
            missingPaymentTimes, missingRefundTimes, unverifiedDineInRefunds);
    }
}
