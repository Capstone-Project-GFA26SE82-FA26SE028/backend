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
    private static readonly IReadOnlyDictionary<string, string> Notices = new Dictionary<string, string>
    {
        ["food_information"] = AssistantRules.MissingFoodInformation,
        ["food_concern"] = "If you suspect a problem with your food, stop eating it and contact a staff member for assistance. I cannot confirm the cause or contact staff on your behalf.",
        ["restaurant"] = AssistantRules.MissingRestaurantInformation,
        ["out_of_scope"] = "I can help with the public menu, prices and dish availability. For other restaurant matters, please contact a staff member.",
        ["no_match"] = "I could not find a matching dish in the current selection. Could you try a dish name or ask a staff member?"
    };

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
            return await FoodNotice(request, ct);
        }

        var source = await menu.Read(request.Guest, ct);
        var query = string.Join(" ", request.History.Select(turn => turn.Content).Append(request.Message));
        var candidates = menu.Candidates(source.Items, query);
        var keyed = AssistantRules.Keyed(candidates);
        var evidence = AssistantRules.ModelItems(keyed);
        var limited = source.TotalCount > candidates.Count;
        var plan = await model.ChatAsync(new ChatModelRequestDTO(
            request.Message, request.History, evidence, limited, Notices, settings.Currency), ct);
        if (plan is null)
        {
            throw new AIException(502, "The assistant returned no menu selection.");
        }

        ChatResponseComposer.ValidateLanguage(plan.Language);
        if (plan.Intent is not ("menu" or "recommendation" or "food_information" or "food_concern" or "restaurant" or "out_of_scope")
            || plan.Topic is not ("overview" or "price" or "availability" or "options")
            || plan.InformationKeys is null || plan.RecommendationKeys is null || plan.MaximumPrice is < 0
            || plan.InformationKeys.Count > settings.MaximumSuggestions || plan.RecommendationKeys.Count > settings.MaximumSuggestions
            || plan.InformationKeys.Concat(plan.RecommendationKeys).Any(key => string.IsNullOrWhiteSpace(key) || !keyed.ContainsKey(key))
            || plan.InformationKeys.Distinct().Count() != plan.InformationKeys.Count
            || plan.RecommendationKeys.Distinct().Count() != plan.RecommendationKeys.Count
            || plan.RecommendationKeys.Any(key => !evidence.Single(item => item.Key == key).CanOrder))
        {
            throw new AIException(502, "The assistant returned an unverified menu answer.");
        }

        await guests.VerifyAsync(request.Guest, ct);
        if (plan.Intent == "recommendation"
            && request.History.Any(turn => turn.Role == "user" && AssistantRules.NeedsVerifiedFoodInformation(turn.Content)))
        {
            throw new AIException(502, "The assistant cannot recommend dishes without verified dietary information.");
        }

        if (plan.Intent is not ("menu" or "recommendation"))
        {
            if (plan.InformationKeys.Count != 0 || plan.RecommendationKeys.Count != 0)
            {
                throw new AIException(502, "The assistant attached unsupported dishes to a notice.");
            }

            return Result(ChatResponseComposer.Notice(plan.Language, plan.Intent, limited), [], limited);
        }

        var keys = plan.Intent == "recommendation" ? plan.RecommendationKeys : plan.InformationKeys;
        var budget = AssistantRules.NumericBudget(request.Message) ?? plan.MaximumPrice;
        var refreshed = await Task.WhenAll(keys.Select(async key =>
        {
            var item = await menu.Refresh(request.Guest, keyed[key].ItemId, ct);
            if (item is null)
            {
                throw new AIException(409, "The menu changed while answering. Please ask again.");
            }

            var current = AssistantRules.ModelItems(new Dictionary<string, MenuItemDTO>
            {
                [key] = item
            })[0];
            if (current != evidence.Single(value => value.Key == key))
            {
                throw new AIException(409, "The menu changed while answering. Please ask again.");
            }

            return item;
        }));
        await guests.VerifyAsync(request.Guest, ct);
        var matchingItems = budget.HasValue
            ? refreshed.Where(item => item.Price.HasValue && item.Price <= budget.Value).ToArray()
            : refreshed;
        var topic = AssistantRules.ResolveMenuTopic(request.Message, plan.Topic);
        return Result(ChatResponseComposer.Menu(plan.Language, topic, matchingItems, limited), matchingItems, limited);
    }

    private async Task<ChatResponseDTO> FoodNotice(ChatRequestDTO request, CancellationToken ct)
    {
        var plan = await model.ChatAsync(new ChatModelRequestDTO(
            request.Message, request.History, [], false, Notices, options.Value.Currency), ct);
        if (plan is null)
        {
            throw new AIException(502, "The assistant returned an unverified answer.");
        }

        ChatResponseComposer.ValidateLanguage(plan.Language);
        await guests.VerifyAsync(request.Guest, ct);
        return Result(ChatResponseComposer.Notice(plan.Language, "food_information", false), [], false);
    }

    private ChatResponseDTO Result(string answer, IReadOnlyList<MenuItemDTO> items, bool limited)
    {
        return new ChatResponseDTO(answer, items, limited, clock.GetUtcNow());
    }
}
