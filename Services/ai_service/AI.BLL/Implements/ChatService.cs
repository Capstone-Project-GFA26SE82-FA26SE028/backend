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
    IChatContextToken tokens,
    IOptions<AISettings> options,
    TimeProvider clock) : IChatService
{
    public async Task<ChatResponseDTO> Chat(ChatRequestDTO request, CancellationToken ct)
    {
        AssistantRules.ValidateGuest(request.Guest);
        Validate(request);

        var guest = await guests.VerifyAsync(request.Guest, ct);
        var state = request.ConversationContext is null ? new ChatContextDTO() : tokens.Read(request.ConversationContext, guest);
        var question = ChatConversation.Fold(request.Message);
        var detectedLanguage = ChatConversation.PreferredLanguage(request.Message);
        var language = detectedLanguage.Length > 0 ? detectedLanguage : state.Language;
        state = state with { Language = language };
        var foodFacts = ChatConversation.AsksFoodFacts(question);
        var itemFacts = ChatConversation.AsksItemFacts(question);
        if (foodFacts && !itemFacts && (request.ConversationContext is not null || request.History.Count == 0))
        {
            state = ChatConversation.Update(state, question, []);
            return Reply(ChatText.MissingFoodFacts(language), [], false, state, guest);
        }

        var source = await menu.Read(request.Guest, guest.RestaurantId, ct);
        if (request.ConversationContext is null)
        {
            state = ChatConversation.FromHistory(request.History, source.Items);
            language = detectedLanguage.Length > 0 ? detectedLanguage : state.Language;
            state = state with { Language = language };
        }

        var named = ChatConversation.NamedItems(question, source.Items);
        var (references, ambiguous) = ChatConversation.References(question, named, state.PresentedItems);
        var excluding = ChatConversation.ExcludesDishes(question);
        state = ChatConversation.Update(state, question, references);
        if (foodFacts && !itemFacts)
        {
            return Reply(ChatText.MissingFoodFacts(language), [], false, state, guest);
        }

        var candidates = await Candidates(request, guest, source.Items, state, references, ct);
        var keyed = AssistantRules.Keyed(candidates);
        var modelRequest = ModelRequest(request, state, keyed, references);
        var plan = await model.ChatAsync(modelRequest, ct);
        ValidatePlan(plan, keyed);
        AssistantRules.RequireSameGuest(guest, await guests.VerifyAsync(request.Guest, ct));

        var neutralLanguage = ChatConversation.IsLanguageNeutral(request.Message);
        if (plan.Language == "other" && detectedLanguage.Length == 0 && !neutralLanguage)
        {
            return Reply("This chat supports English and Vietnamese. Please ask in either language.", [], false, state, guest);
        }

        if (detectedLanguage.Length == 0 && !neutralLanguage)
        {
            language = plan.Language;
        }

        state = state with { Language = language };

        var limited = source.TotalCount > candidates.Count;
        if (plan.Intent is "out_of_scope" or "restaurant" or "food_concern" or "greeting")
        {
            return Reply(NonMenuReply(plan.Intent, language), [], limited, state, guest);
        }

        if (ambiguous || plan.Intent == "clarify" && references.Count == 0)
        {
            return Reply(ChatText.Pick(language, "Which dish do you mean? Please give its name or position in my previous list.",
                "Bạn muốn hỏi món nào? Bạn có thể nhắc tên hoặc số thứ tự của món trong câu trả lời trước."), [], limited, state, guest);
        }

        if (excluding && references.Count > 0 && !ChatConversation.AsksForAlternatives(question) && !itemFacts)
        {
            return Reply(ChatText.Pick(language, "I’ll leave that dish out of future suggestions.",
                "Mình sẽ bỏ món đó khỏi các gợi ý tiếp theo."), [], limited, state, guest);
        }

        if (plan.RememberFoodRestriction && !ChatConversation.ClearsDiet(question))
        {
            state = state with { HasDietaryRestriction = true };
        }

        var uncertainFood = foodFacts || plan.NeedsFoodVerification || plan.RememberFoodRestriction || plan.Intent == "food_information";
        var wantsSelection = ChatConversation.WantsSelection(question);
        var wantsCheaper = ChatConversation.WantsCheaper(question);
        var informationRequested = (references.Count > 0 && (itemFacts || !wantsSelection)
                || plan.Intent == "menu" && !wantsSelection) && !(wantsCheaper && wantsSelection);
        var recommend = !informationRequested && (plan.Intent == "recommendation" || ChatConversation.WantsSelection(question));
        if ((uncertainFood && !informationRequested) || (recommend && (state.HasAllergyConcern || state.HasDietaryRestriction)))
        {
            return Reply(ChatText.MissingFoodFacts(language), [], limited, state, guest);
        }

        if (recommend && state.Preference == "none" && plan.Preference != "none"
            && ChatConversation.RequestedPreference(question) is null)
        {
            state = state with { Preference = plan.Preference };
        }

        if (recommend && plan.MaximumPrice is decimal maximum && state.MaximumPrice is null && question.Any(char.IsDigit))
        {
            state = state with { MaximumPrice = maximum };
        }

        decimal? cheaperThan = null;
        if (recommend && wantsCheaper)
        {
            var comparison = references.Count > 0 ? references : state.PresentedItems;
            if (comparison.Count == 1 && comparison[0] != Guid.Empty)
            {
                var prior = await menu.Refresh(request.Guest, guest.RestaurantId, comparison[0], ct);
                cheaperThan = prior?.Price;
            }

            if (cheaperThan is null)
            {
                return Reply(ChatText.Pick(language, "Which dish should I compare prices with?",
                    "Bạn muốn so giá với món nào?"), [], limited, state, guest);
            }
        }

        var ids = SelectItems(plan, keyed, references, state, informationRequested, recommend, cheaperThan);
        var selected = await ReadSelected(request, guest, ids, state, recommend, cheaperThan, ct);
        var answer = Answer(selected, language, state, recommend, uncertainFood, limited);
        if (informationRequested && itemFacts && ChatConversation.AsksForAdditionalSuggestions(question)
            && !uncertainFood && !state.HasAllergyConcern && !state.HasDietaryRestriction)
        {
            var suggestionIds = SelectItems(plan, keyed, [], state, false, true, null)
                .Except(selected.Select(item => item.ItemId)).ToArray();
            var suggestions = await ReadSelected(request, guest, suggestionIds, state, true, null, ct);
            var additional = suggestions.Take(Math.Max(0, options.Value.MaximumSuggestions - selected.Count)).ToArray();
            if (additional.Length > 0)
            {
                answer += ChatText.Pick(language, "\nSuggestions:\n", "\nGợi ý thêm:\n")
                    + string.Join("\n", additional.Select(item => ChatText.Item(item, language, null, false)));
                selected.AddRange(additional);
            }
            else
            {
                answer += ChatText.Pick(language, "\nI couldn't find an additional verified match.",
                    "\nMình chưa tìm được món phù hợp để gợi ý thêm.");
            }
        }

        return Reply(answer, selected, limited, state, guest);
    }

    private void Validate(ChatRequestDTO request)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > settings.MaximumMessageLength
            || request.History.Count > settings.MaximumHistoryTurns
            || request.History.Any(turn => turn.Role is not ("user" or "assistant")
                || string.IsNullOrWhiteSpace(turn.Content) || turn.Content.Length > settings.MaximumMessageLength)
            || request.ConversationContext is not null && string.IsNullOrWhiteSpace(request.ConversationContext))
        {
            throw new AIException(400, "Provide a message and bounded user/assistant history within the configured limits.");
        }
    }

    private async Task<IReadOnlyList<MenuItemDTO>> Candidates(ChatRequestDTO request, VerifiedGuest guest,
        IReadOnlyList<MenuItemDTO> items, ChatContextDTO state, IReadOnlyList<Guid> references, CancellationToken ct)
    {
        var menuById = items.ToDictionary(item => item.ItemId);
        var prioritized = new List<MenuItemDTO>();
        foreach (var id in references.Concat(state.PresentedItems).Distinct())
        {
            if (id == Guid.Empty || prioritized.Count >= options.Value.ModelCandidateCount)
            {
                continue;
            }

            var item = menuById.GetValueOrDefault(id)
                ?? await menu.Refresh(request.Guest, guest.RestaurantId, id, ct);
            if (item is not null)
            {
                prioritized.Add(item);
            }
        }

        var ranking = menu.Candidates(items, request.Message);
        var eligible = ranking.Where(item => !ChatConversation.IsExcluded(item, state)
            && ChatConversation.MatchesPreference(item, state));
        var described = state.Preference == "none" ? Enumerable.Empty<MenuItemDTO>() : items.Where(item =>
            !ChatConversation.IsExcluded(item, state) && ChatConversation.MatchesPreference(item, state));
        return prioritized.Concat(eligible).Concat(described).Concat(ranking)
            .DistinctBy(item => item.ItemId).Take(options.Value.ModelCandidateCount).ToArray();
    }

    private static ChatModelRequestDTO ModelRequest(ChatRequestDTO request, ChatContextDTO state,
        IReadOnlyDictionary<string, MenuItemDTO> keyed, IReadOnlyList<Guid> references)
    {
        var keysById = keyed.ToDictionary(entry => entry.Value.ItemId, entry => entry.Key);
        return new ChatModelRequestDTO(request.Message,
            request.History.Where(turn => turn.Role == "user").TakeLast(2)
                .Select(turn => turn with { Content = turn.Content[..Math.Min(180, turn.Content.Length)] }).ToArray(),
            AssistantRules.ModelItems(keyed))
        {
            PreviousItems = state.PresentedItems.Select(id => keysById.GetValueOrDefault(id)).ToArray(),
            ReferencedItems = references.Where(keysById.ContainsKey).Select(id => keysById[id]).ToArray(),
            ExcludedItems = state.ExcludedItems.Where(keysById.ContainsKey).Select(id => keysById[id]).ToArray(),
            ExcludedTerms = state.ExcludedTerms,
            RequiresLightMeal = state.RequiresLightMeal,
            Preference = state.Preference,
            DefaultLanguage = state.Language,
            MaximumPrice = state.MaximumPrice
        };
    }

    private void ValidatePlan(ChatPlanDTO plan, IReadOnlyDictionary<string, MenuItemDTO> keyed)
    {
        if (plan is null || plan.Intent is not ("menu" or "recommendation" or "food_information" or "food_concern"
                or "restaurant" or "out_of_scope" or "clarify" or "greeting")
            || plan.Language is not ("en" or "vi" or "other") || plan.Preference is not ("none" or "warm" or "cool" or "light")
            || plan.InformationKeys is null || plan.RecommendationKeys is null || plan.MaximumPrice is < 0
            || plan.InformationKeys.Count > options.Value.MaximumSuggestions || plan.RecommendationKeys.Count > options.Value.MaximumSuggestions
            || plan.InformationKeys.Concat(plan.RecommendationKeys).Any(key => string.IsNullOrWhiteSpace(key) || !keyed.ContainsKey(key))
            || plan.InformationKeys.Distinct().Count() != plan.InformationKeys.Count
            || plan.RecommendationKeys.Distinct().Count() != plan.RecommendationKeys.Count
            || plan.RecommendationKeys.Any(key => !AssistantRules.CanRecommend(keyed[key])))
        {
            throw new AIException(502, "The model returned an invalid menu selection.");
        }
    }

    private static IReadOnlyList<Guid> SelectItems(ChatPlanDTO plan, IReadOnlyDictionary<string, MenuItemDTO> keyed,
        IReadOnlyList<Guid> references, ChatContextDTO state, bool information, bool recommend, decimal? cheaperThan)
    {
        if (information && references.Count > 0)
        {
            return references;
        }

        var keys = recommend ? plan.RecommendationKeys : plan.InformationKeys;
        var ids = keys.Select(key => keyed[key].ItemId);
        if (recommend && (state.Preference != "none" || state.RequiresLightMeal || cheaperThan.HasValue))
        {
            ids = ids.Concat(keyed.Values.Where(item => AssistantRules.CanRecommend(item)
                && ChatConversation.MatchesPreference(item, state)
                && ChatConversation.WithinBudget(item, state, cheaperThan)).Select(item => item.ItemId));
        }

        return ids.Distinct().ToArray();
    }

    private async Task<List<MenuItemDTO>> ReadSelected(ChatRequestDTO request, VerifiedGuest guest, IReadOnlyList<Guid> ids,
        ChatContextDTO state, bool recommend, decimal? cheaperThan, CancellationToken ct)
    {
        var selected = new List<MenuItemDTO>();
        foreach (var id in ids)
        {
            if (recommend && state.ExcludedItems.Contains(id))
            {
                continue;
            }

            var item = await menu.Refresh(request.Guest, guest.RestaurantId, id, ct);
            if (item is null || recommend && (!AssistantRules.CanRecommend(item)
                || !ChatConversation.MatchesPreference(item, state) || ChatConversation.IsExcluded(item, state)
                || !ChatConversation.WithinBudget(item, state, cheaperThan)))
            {
                continue;
            }

            selected.Add(item);
            if (selected.Count == options.Value.MaximumSuggestions)
            {
                break;
            }
        }

        return selected;
    }

    private static string Answer(IReadOnlyList<MenuItemDTO> items, string language, ChatContextDTO state,
        bool recommend, bool foodFacts, bool limited)
    {
        var answer = items.Count > 0
            ? string.Join("\n", items.Select(item => ChatText.Item(item, language, recommend ? null : state.MaximumPrice, state.StrictPriceLimit)))
            : ChatText.Pick(language, "I couldn't find a verified match in the current menu. You can try a dish name or ask a staff member.",
                "Mình chưa tìm được món phù hợp trong thực đơn hiện tại. Bạn có thể nhắc tên món hoặc hỏi nhân viên.");
        if (foodFacts)
        {
            answer += "\n" + ChatText.MissingFoodFacts(language);
        }

        if (limited)
        {
            answer += ChatText.Pick(language, "\nThis search covers a limited selection of the public menu.",
                "\nKết quả này chỉ bao gồm một phần của thực đơn.");
        }

        return answer;
    }

    private static string NonMenuReply(string intent, string language)
    {
        return intent switch
        {
            "food_concern" => ChatText.Pick(language, "If you suspect a problem with your food, stop eating it and contact a staff member for assistance.",
                "Nếu bạn nghi món ăn có vấn đề, hãy ngừng ăn và báo nhân viên để được hỗ trợ."),
            "restaurant" => ChatText.Pick(language, AssistantRules.MissingRestaurantInformation,
                "Mình chưa có thông tin xác nhận về giờ mở cửa, liên hệ hoặc chính sách của nhà hàng. Bạn vui lòng hỏi nhân viên."),
            "greeting" => ChatText.Pick(language, "I can help you find dishes and check prices or availability. What would you like?",
                "Mình có thể giúp bạn tìm món, xem giá hoặc kiểm tra món còn đặt được không. Bạn muốn hỏi gì?"),
            _ => ChatText.Pick(language, "I can help with the public menu, prices and dish availability. For other matters, please ask a staff member.",
                "Mình có thể hỗ trợ về thực đơn, giá và tình trạng món của nhà hàng.")
        };
    }

    private ChatResponseDTO Reply(string answer, IReadOnlyList<MenuItemDTO> items, bool limited,
        ChatContextDTO state, VerifiedGuest guest)
    {
        if (items.Count > 0)
        {
            state = state with { PresentedItems = items.Select(item => item.ItemId).ToArray() };
        }

        return new ChatResponseDTO(answer, items, limited, clock.GetUtcNow(), tokens.Write(state, guest));
    }
}
