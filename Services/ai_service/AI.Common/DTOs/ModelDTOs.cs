using System.Text.Json.Serialization;

namespace AI.Common.DTOs;

public sealed record ModelItemDTO(string Key, string Name, string Category, string? Description, decimal? Price, bool CanOrder);

public sealed record ModelCartLineDTO(string Name, string Category, int Quantity);

public sealed record ChatModelRequestDTO(string Message, IReadOnlyList<ConversationTurn> History, IReadOnlyList<ModelItemDTO> Menu)
{
    public IReadOnlyList<string?> PreviousItems { get; init; } = [];
    public IReadOnlyList<string> ReferencedItems { get; init; } = [];
    public IReadOnlyList<string> ExcludedItems { get; init; } = [];
    public IReadOnlyList<string> ExcludedTerms { get; init; } = [];
    public bool RequiresLightMeal { get; init; }
    public string Preference { get; init; } = "none";
    public string DefaultLanguage { get; init; } = "vi";
    public decimal? MaximumPrice { get; init; }
}

public sealed record RecommendationModelRequestDTO(
    IReadOnlyList<ModelCartLineDTO> Cart,
    IReadOnlyList<ModelItemDTO> Candidates,
    int Limit);

public sealed class ChatPlanDTO
{
    [JsonRequired]
    public string Intent { get; set; } = string.Empty;
    [JsonRequired]
    public string Language { get; set; } = "en";
    [JsonRequired]
    public string Preference { get; set; } = "none";
    [JsonRequired]
    public bool NeedsFoodVerification { get; set; }
    [JsonRequired]
    public bool RememberFoodRestriction { get; set; }
    [JsonRequired]
    public List<string> InformationKeys { get; set; } = [];
    [JsonRequired]
    public List<string> RecommendationKeys { get; set; } = [];
    [JsonRequired]
    public decimal? MaximumPrice { get; set; }
}

public sealed class ModelSuggestionDTO
{
    public string Key { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public static class RecommendationReasons
{
    public static IReadOnlyList<string> Allowed { get; } = Array.AsReadOnly(new[]
    {
        "A suggested accompaniment to your current selection.",
        "An additional menu choice to consider with your meal.",
        "A suggestion based on the dishes in your cart."
    });
}
