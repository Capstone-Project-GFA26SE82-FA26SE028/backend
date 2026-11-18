namespace Ordering.Common.DTOs;

public sealed record OrderingActor(Guid SessionId, Guid? TableId, Guid? UserId, Guid? GuestId, DateTimeOffset? ExpiresAt)
{
    public Guid? RestaurantId { get; init; }
    public Guid? PaidCheckoutId { get; init; }

    public string Channel { get; init; } = "DineIn";

    public bool IsGuest
    {
        get { return GuestId.HasValue; }
    }

    public string Scope
    {
        get { return $"{(IsGuest ? "g" : "w")}:{(GuestId ?? UserId):N}:{SessionId:N}:"; }
    }
}
