namespace Shared.Common.Billing;

public sealed record PaidSessionSettlement(Guid TableSessionId, Guid RestaurantId, Guid BillId,
    Guid CheckoutId, DateTimeOffset PaidAt);
