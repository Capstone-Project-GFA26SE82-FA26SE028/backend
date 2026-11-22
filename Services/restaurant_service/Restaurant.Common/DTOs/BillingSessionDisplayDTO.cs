namespace Restaurant.Common.DTOs;

public sealed record BillingTableDisplayDTO(Guid TableId, string TableCode, Guid AreaId, string AreaName);

public sealed record BillingSessionDisplayDTO(Guid TableSessionId, Guid RestaurantId, int PartySize,
    DateTimeOffset OpenedAt, IReadOnlyList<BillingTableDisplayDTO> Tables);
