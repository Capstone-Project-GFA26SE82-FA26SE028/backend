namespace Billing.Common.DTOs;

public sealed class BillingAccessScope
{
    public Guid UserId { get; set; }
    public string RoleId { get; set; } = string.Empty;
    public bool IsAdministrator { get; set; }
    public IReadOnlyList<Guid> RestaurantIds { get; set; } = [];
}
