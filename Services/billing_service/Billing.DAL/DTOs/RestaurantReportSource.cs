using Billing.DAL.Models;

namespace Billing.DAL.DTOs;

// A read result, not a persisted entity or a new database table.
public sealed record RestaurantReportSource(
    IReadOnlyList<Payment> Payments,
    IReadOnlyList<OnlinePayment> OnlineSales,
    IReadOnlyList<OnlinePayment> OnlineRefunds,
    int MissingPaymentTimes,
    int MissingRefundTimes,
    int UnverifiedDineInRefunds);
