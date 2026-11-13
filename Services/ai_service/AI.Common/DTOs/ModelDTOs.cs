namespace AI.Common.DTOs;

public sealed record ModelItemDTO(string Key, string Name, string Category, string? Description, decimal? Price, bool CanOrder);

public sealed record ModelCartLineDTO(string Name, string Category, int Quantity);

public sealed record ChatModelRequestDTO(
    string Message,
    IReadOnlyList<ConversationTurn> History,
    IReadOnlyList<ModelItemDTO> Menu,
    bool SearchLimited = false,
    IReadOnlyDictionary<string, string>? Notices = null,
    string? Currency = null);

public sealed record ChatAnswerRequestDTO(
    string Message,
    IReadOnlyList<ConversationTurn> History,
    string Intent,
    IReadOnlyList<ModelItemDTO> VerifiedMenu,
    bool SearchLimited,
    string? VerifiedNotice = null);

public sealed class ChatAnswerDTO
{
    public string Answer { get; set; } = string.Empty;
    public List<string> UsedKeys { get; set; } = [];
}

public sealed record RecommendationModelRequestDTO(
    IReadOnlyList<ModelCartLineDTO> Cart,
    IReadOnlyList<ModelItemDTO> Candidates,
    int Limit);

public sealed class ChatPlanDTO
{
    public string Language { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public List<string> UsedKeys { get; set; } = [];
    public string Intent { get; set; } = string.Empty;
    public List<string> InformationKeys { get; set; } = [];
    public List<string> RecommendationKeys { get; set; } = [];
    public decimal? MaximumPrice { get; set; }
}

public sealed class ModelSuggestionDTO
{
    public string Key { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public static class RecommendationReasons
{
    private static readonly IReadOnlyDictionary<string, string> VietnameseReasons = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["A suggested accompaniment to your current selection."] = "Món ăn kèm được gợi ý cho các món bạn đã chọn.",
        ["An additional menu choice to consider with your meal."] = "Một lựa chọn thêm cho bữa ăn của bạn.",
        ["A suggestion based on the dishes in your cart."] = "Gợi ý dựa trên các món trong giỏ hàng của bạn."
    };

    public static IReadOnlyList<string> Allowed { get; } = Array.AsReadOnly(VietnameseReasons.Keys.ToArray());

    public static string ToVietnamese(string reason)
    {
        return VietnameseReasons[reason];
    }
}
