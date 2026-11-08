using System.ComponentModel.DataAnnotations;

namespace ApiGateway.DTOs.AI;

public sealed class ConversationTurnDTO
{
    [Required]
    [RegularExpression("^(user|assistant)$")]
    public string Role { get; set; } = string.Empty;

    [Required]
    [StringLength(1000)]
    public string Content { get; set; } = string.Empty;
}

public sealed class ChatRequestDTO
{
    [Required]
    [StringLength(1000)]
    public string Message { get; set; } = string.Empty;

    [MaxLength(6)]
    public List<ConversationTurnDTO> History { get; set; } = [];
}

public sealed class RecommendationRequestDTO
{
    [Range(1, 3)]
    public int Limit { get; set; } = 3;
}

public sealed class MenuCardDTO
{
    public Guid ItemId { get; set; }
    public Guid CategoryId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
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

public sealed record ChatResponseDTO(string Answer, IReadOnlyList<MenuCardDTO> Items, bool SearchLimited, DateTimeOffset EvaluatedAt);

public sealed record SuggestedItemDTO(MenuCardDTO Item, string Reason);

public sealed record RecommendationResponseDTO(Guid CartId, string CartVersion, string Message, IReadOnlyList<SuggestedItemDTO> Items, bool SearchLimited, DateTimeOffset EvaluatedAt);
