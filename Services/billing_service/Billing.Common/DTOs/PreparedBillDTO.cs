namespace Billing.Common.DTOs;

public sealed record PreparedBillDTO(Guid CheckoutId, Guid TableSessionId, string CheckoutStatus, BillDTO Bill,
    Guid PreparedByRef, DateTimeOffset PreparedAt, DateTimeOffset? PresentedAt);
