using System.Globalization;
using AI.BLL.Interfaces;
using AI.Common.DTOs;
using AI.Common.Exceptions;
using AI.Common.Settings;
using Microsoft.Extensions.Options;

namespace AI.BLL.Implements;

public sealed class ChatService(
    IGuestAccessClient guests,
    MenuContextReader menu,
    IAssistantModelClient model,
    IOptions<AISettings> options,
    TimeProvider clock) : IChatService
{
    public async Task<ChatResponseDTO> Chat(ChatRequestDTO request, CancellationToken ct)
    {
        AssistantRules.ValidateGuest(request.Guest);
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > settings.MaximumMessageLength
            || request.History.Count > settings.MaximumHistoryTurns
            || request.History.Any(turn => turn.Role is not ("user" or "assistant")
                || string.IsNullOrWhiteSpace(turn.Content) || turn.Content.Length > settings.MaximumMessageLength))
        {
            throw new AIException(400, "Provide a message and bounded user/assistant history within the configured limits.");
        }

        await guests.VerifyAsync(request.Guest, ct);
        if (AssistantRules.NeedsVerifiedFoodInformation(request.Message))
        {
            return Result(AssistantRules.MissingFoodInformation, [], false);
        }

        var source = await menu.Read(request.Guest, ct);
        var query = string.Join(" ", request.History.Select(turn => turn.Content).Append(request.Message));
        var candidates = menu.Candidates(source.Items, query);
        var keyed = AssistantRules.Keyed(candidates);
        var limited = source.TotalCount > candidates.Count;
        var plan = await model.ChatAsync(new ChatModelRequestDTO(request.Message, request.History, AssistantRules.ModelItems(keyed)), ct);
        if (plan is null || plan.Intent is not ("menu" or "recommendation" or "food_information" or "food_concern" or "restaurant" or "out_of_scope")
            || plan.InformationKeys is null || plan.RecommendationKeys is null || plan.MaximumPrice is < 0
            || plan.InformationKeys.Count > settings.MaximumSuggestions || plan.RecommendationKeys.Count > settings.MaximumSuggestions
            || plan.InformationKeys.Concat(plan.RecommendationKeys).Any(key => string.IsNullOrWhiteSpace(key) || !keyed.ContainsKey(key))
            || plan.InformationKeys.Distinct().Count() != plan.InformationKeys.Count
            || plan.RecommendationKeys.Distinct().Count() != plan.RecommendationKeys.Count
            || plan.RecommendationKeys.Any(key => !AssistantRules.CanRecommend(keyed[key])))
        {
            throw new AIException(502, "The model returned an invalid menu selection.");
        }

        await guests.VerifyAsync(request.Guest, ct);
        if (plan.Intent == "food_information" || (plan.Intent == "recommendation"
            && request.History.Any(turn => turn.Role == "user" && AssistantRules.NeedsVerifiedFoodInformation(turn.Content))))
        {
            return Result(AssistantRules.MissingFoodInformation, [], limited);
        }

        if (plan.Intent == "food_concern")
        {
            return Result("If you suspect a problem with your food, stop eating it and contact a staff member for assistance. I cannot confirm the cause or contact staff on your behalf.", [], limited);
        }

        if (plan.Intent == "restaurant")
        {
            return Result(AssistantRules.MissingRestaurantInformation, [], limited);
        }

        if (plan.Intent == "out_of_scope")
        {
            return Result("I can help with the public menu, prices and dish availability. Please ask your question in English. For other restaurant matters, please contact a staff member.", [], limited);
        }

        var selected = new List<MenuItemDTO>();
        var keys = plan.Intent == "recommendation" ? plan.RecommendationKeys : plan.InformationKeys;
        var budget = AssistantRules.NumericBudget(request.Message) ?? plan.MaximumPrice;
        foreach (var key in keys)
        {
            var item = await menu.Refresh(request.Guest, keyed[key].ItemId, ct);
            if (item is null || (plan.Intent == "recommendation" && !AssistantRules.CanRecommend(item))
                || (budget.HasValue && (!item.Price.HasValue || item.Price > budget)))
            {
                continue;
            }

            selected.Add(item);
        }

        var answer = selected.Count == 0
            ? "No matching dishes were found in the current search. Please try a dish name or ask a staff member."
            : string.Join("\n", selected.Select(item =>
                $"{item.ItemName}: {(item.Price.HasValue ? item.Price.Value.ToString("0.00", CultureInfo.InvariantCulture) : "price not available")}. {(item.CanOrder ? "Available to order." : "Not currently available to order.")}"));
        if (limited)
        {
            answer += "\nThis search covers a limited selection of the public menu. Try a specific dish name to narrow your search.";
        }

        return Result(answer, selected, limited);
    }

    private ChatResponseDTO Result(string answer, IReadOnlyList<MenuItemDTO> items, bool limited)
    {
        return new ChatResponseDTO(answer, items, limited, clock.GetUtcNow());
    }
}
