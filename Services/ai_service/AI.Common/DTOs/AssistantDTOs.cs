namespace AI.Common.DTOs;

public sealed record GuestContext(Guid TableSessionId, string Token);

public sealed record VerifiedGuest(Guid TableSessionId, Guid GuestAccessId, DateTimeOffset ExpiresAt);

public sealed record ConversationTurn(string Role, string Content);

public sealed record ChatRequestDTO(GuestContext Guest, string Message, IReadOnlyList<ConversationTurn> History);

public sealed record RecommendationRequestDTO(GuestContext Guest, Guid CartId, int Limit);

public sealed record MenuPageDTO(IReadOnlyList<MenuItemDTO> Items, int TotalCount);

public sealed class MenuItemDTO
{
    public Guid ItemId { get; set; }
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public decimal? Price { get; set; }
    public Guid? PriceId { get; set; }
    public string Availability { get; set; } = string.Empty;
    public bool CanOrder { get; set; }
    public string? UnavailableReason { get; set; }
    public string Channel { get; set; } = string.Empty;
    public DateTimeOffset EvaluatedAt { get; set; }
    public bool RequiresOptionSelection { get; set; }
}

public sealed record CartLineDTO(Guid ItemId, int Quantity);

public sealed record CartContextDTO(
    Guid CartId,
    Guid TableSessionId,
    DateTimeOffset ExpiresAt,
    string Version,
    IReadOnlyList<CartLineDTO> Items);

public sealed record SuggestedItemDTO(MenuItemDTO Item, string Reason);

public sealed record ChatResponseDTO(string Answer, IReadOnlyList<MenuItemDTO> Items, bool SearchLimited, DateTimeOffset EvaluatedAt);

public sealed record RecommendationResponseDTO(
    Guid CartId,
    string CartVersion,
    string Message,
    IReadOnlyList<SuggestedItemDTO> Items,
    bool SearchLimited,
    DateTimeOffset EvaluatedAt);
