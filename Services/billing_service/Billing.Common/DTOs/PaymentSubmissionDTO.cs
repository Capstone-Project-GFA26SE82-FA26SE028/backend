namespace Billing.Common.DTOs;

public sealed record PaymentSubmissionDTO(Guid? CheckoutId, Guid TableSessionId, Guid CashRegisterId,
    DateTimeOffset? SubmittedAt, string Status, Guid? BillId = null);
