using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AI.Common.DTOs;

namespace AI.BLL.Implements;

internal static class ChatConversation
{
    private const int MaximumExclusions = 24;
    private const int MaximumPriorItems = 3;
    private const string RestoreVietnamese = @"(?:goi y lai|cho lai|co the chon lai)";
    private const string RestoreEnglish = @"(?:include|allow|suggest)(?=\s+[\p{L}\p{N} ]+\s+again\b)";
    private const string UnwantedVietnamese = @"khong muon(?: (?:an|uong|dung))?";

    private static readonly Regex Ordinal = Pattern(@"\b(?:mon (?:thu |so )?(?<vi>mot|hai|ba|nhat|cuoi|tu|bon|nam|[0-9]+)|(?<en>first|second|third|last) (?:one|dish|item)|(?:the|your) (?<en>first|second|third|last)|(?:dish|item) (?:number )?(?<vi>[0-9]+))\b");
    private static readonly Regex SingularReference = Pattern(@"\b(?:mon (?:do|ay|nay|(?:(?:ban|ai) )?vua (?:noi|goi y|gioi thieu))|(?:that|this) (?:dish|one|item)|you (?:just )?(?:suggested|recommended|mentioned)|(?:does|is|was|order|costs?) it)\b");
    private static readonly Regex ShortReference = Pattern(@"^\s*(?:vay |the )?(?:con khong|con hang khong|gia bao nhieu|bao nhieu|dat duoc khong|how much|still available|in stock)\s*\??\s*$");
    private static readonly Regex MultipleReference = Pattern(@"\b(?:ca hai mon|hai mon do|nhung mon do|both (?:ones|dishes|items)|those (?:dishes|items))\b");
    private static readonly Regex Drop = Pattern($@"\b(?:bo (?:mon|no|di)|dung (?:goi y|chon)|{UnwantedVietnamese}|remove|exclude|avoid|skip|don.t suggest)\b");
    private static readonly Regex DishGroup = Pattern($@"\b(?:{RestoreVietnamese}|{RestoreEnglish}|dung goi y|khong goi y|{UnwantedVietnamese}|tranh|bo|exclude|avoid|skip|don.t suggest)\s+(?:cac mon |nhung mon |mon |any )?(?<name>[\p{{L}}\p{{N}} ]+?)(?=\s+(?:nua|anymore|any more|again|please|for me|cho minh|cho toi|giup minh|va|nhung)\b|[.!?,;]|$)");
    private static readonly Regex PresentedLine = Pattern(@"^.+:\s*(?:[0-9]|price not available|chua co gia)");
    private static readonly Regex Restore = Pattern($@"\b(?:{RestoreVietnamese}|{RestoreEnglish})\b");
    private static readonly Regex ClearBudget = Pattern(@"\b(?:khong (?:gioi han|can gioi han) gia|bo gioi han (?:gia|ngan sach)|ngan sach khong gioi han|no (?:price limit|budget limit)|any price|remove the budget)\b");
    private static readonly Regex Budget = Pattern(@"\b(?<op>duoi|toi da|khong qua|khong vuot qua|tam|khoang|ngan sach(?: la)?|under|below|less than|up to|at most|budget(?: of| is)?|around|about)\s*(?<amount>[0-9]+(?:[.,][0-9]{3})*)(?:\s*(?<scale>k|nghin|ngan|thousand))?\b");
    private static readonly Regex FoodFacts = Pattern(@"\b(?:allerg\w*|vegan|vegetarian|halal|kosher|gluten|spicy|spice|ingredient\w*|nut.free|dairy.free|di ung|an chay|thuan chay|(?:it|khong|do|mon|co) cay|cay khong|mon chay|thanh phan|nguyen lieu|co (?:chua |dung )?(?:dau phong|lac|hai san|sua|trung)|moi sung|sung moi|noi me day|noi man|noi ban|kho tho|khong an duoc|cannot eat|can.t eat|throat (?:closes|swells)|lips? swell|break out|food reaction)\b");
    private static readonly Regex Allergy = Pattern(@"\b(?:allerg\w*|di ung|moi sung|sung moi|noi me day|noi man|noi ban|kho tho|throat (?:closes|swells)|lips? swell|break out|food reaction)\b");
    private static readonly Regex DietaryRestriction = Pattern(@"\b(?:khong (?:an|dung) duoc|khong an cay|minh an chay|toi an chay|chi an chay|it cay|i (?:am|m) (?:vegan|vegetarian)|i can.t eat|i cannot eat|no spicy food|not (?:too )?spicy|avoid (?:spice|gluten|dairy))\b");
    private static readonly Regex ClearDiet = Pattern(@"\b(?:bo yeu cau (?:an chay|it cay|khong cay)|khong can (?:an chay|it cay|khong cay) nua|no longer need (?:mild|vegetarian|vegan))\b");
    private static readonly Regex Warm = Pattern(@"\b(?:am bung|am nguoi|nong bung|mon nong|gi nong|warm me up|warm (?:my|the) belly|something (?:warm|hot)|a hot meal)\b");
    private static readonly Regex Cool = Pattern(@"\b(?:mon (?:mat|lanh|nguoi)|gi (?:mat|lanh)|do uong (?:mat|lanh)|something (?:cold|chilled)|a (?:cold|chilled) (?:dish|drink))\b");
    private static readonly Regex AvoidWarm = Pattern(@"\b(?:(?:khong muon|khong thich|tranh|dung (?:goi y|chon)) (?:an |uong )?(?:mon |do uong )?nong|(?:avoid|no|don.t want(?: any)?|don.t suggest) (?:anything |a )?(?:hot|warm)(?: food| dishes| drinks| meals)?)\b");
    private static readonly Regex AvoidCool = Pattern(@"\b(?:(?:khong muon|khong thich|tranh|dung (?:goi y|chon)) (?:an |uong )?(?:mon |do uong )?(?:lanh|mat)|(?:avoid|no|don.t want(?: any)?|don.t suggest) (?:anything |a )?(?:cold|chilled)(?: food| dishes| drinks| meals)?)\b");
    private static readonly Regex Light = Pattern(@"\b(?:nhe bung|an nhe|mon nhe|thanh dam|light (?:meal|dish)|something light)\b");
    private static readonly Regex ClearAllPreferences = Pattern(@"\b(?:mon nao cung duoc|any kind of dish)\b");
    private static readonly Regex ClearTemperaturePreference = Pattern(@"\b(?:khong can (?:mon )?(?:nong|lanh|mat) nua|doesn.t have to be (?:hot|warm|cold|chilled))\b");
    private static readonly Regex ClearLightPreference = Pattern(@"\b(?:khong can (?:mon )?nhe(?: bung)? nua|doesn.t have to be light)\b");
    private static readonly Regex WarmDescription = Pattern(@"\b(?:phuc vu nong|dung nong|an nong|nong hoi|boc khoi|served hot|served warm|piping hot|hot soup|warm broth)\b");
    private static readonly Regex CoolDescription = Pattern(@"\b(?:phuc vu (?:lanh|mat|nguoi)|dung lanh|uong lanh|da lanh|served (?:cold|chilled|cool|at room temperature)|served over ice)\b");
    private static readonly Regex NegatedServingTemperature = Pattern(@"\b(?:khong (?:duoc )?(?:phuc vu|dung|an|uong) (?:nong|lanh|mat|nguoi)|(?:not|never) served (?:hot|warm|cold|chilled|cool))\b");
    private static readonly Regex LightDescription = Pattern(@"\b(?:nhe bung|mon nhe|thanh dam|light meal|light dish|light and fresh)\b");
    private static readonly Regex PriceQuery = Pattern(@"\b(?:gia|bao nhieu|duoi|re hon|price|cost|how much|under|below|cheaper)\b");
    private static readonly Regex AvailabilityQuery = Pattern(@"\b(?:con khong|con (?:hang|dat|ban)|het hang|dat duoc|available|in stock|sold out|can i order)\b");
    private static readonly Regex SelectionQuery = Pattern(@"\b(?:goi y|tim mon|mon nao|co mon|mon gi|lua chon|what .+ (?:order|choose)|recommend|suggest|show (?:me )?(?:dishes|meals|options)|anything|something|alternatives?)\b");
    private static readonly Regex AdditionalSelection = Pattern(
        @"\b(?:va|roi|dong thoi) (?:goi y|cho minh them|cho toi them)\b|\b(?:and|then|also) (?:recommend|suggest|find me)\b"
        + @"|(?:^|[.!?;,])\s*(?:(?:ban |vui long )?(?:goi y them|cho minh them|cho toi them)|(?:please )?(?:recommend|suggest|find me))\b");
    private static readonly Regex Alternatives = Pattern(@"\b(?:goi y (?:mon )?khac|mon khac|thay the|thay vao do|instead|alternative|other (?:dish|option))\b");
    private static readonly Regex Cheaper = Pattern(@"\b(?:re hon|cheaper|less expensive)\b");
    private static readonly Regex Vietnamese = Pattern(@"\b(?:xin chao|cam on|tiep di|minh|chao ban|goi y|mon (?:do|ay|nay|thu|gi|nao|an|nong|nhe)|gia bao nhieu|bao nhieu|con hang|con khong|an gi|am bung|am nguoi|nhe bung|it cay|khong cay|khong an|dau phong|di ung|phuc vu|thuc don|nha hang|duoi|dat duoc|ke chuyen|viet cho|cho hoi)\b");

    private static readonly Regex English = Pattern(@"\b(?:what|which|how|is|are|does|please|i|my|safe|vegetarian|vegan|contain|gluten|spicy|recommend|suggest|price|available|hello|thanks|thank you)\b");
    private static readonly Regex Neutral = Pattern(@"^\s*(?:ok|okay|yes|no|hmm|uh|[0-9]+)\s*[.!?]*\s*$");

    public static bool IsLanguageNeutral(string message)
    {
        return Neutral.IsMatch(Fold(message));
    }

    public static string PreferredLanguage(string message)
    {
        var question = Fold(message);
        if (Vietnamese.IsMatch(question))
        {
            return "vi";
        }

        return English.IsMatch(question) ? "en" : "";
    }

    public static bool AsksFoodFacts(string question)
    {
        return FoodFacts.IsMatch(question) && !ClearDiet.IsMatch(question);
    }

    public static bool ClearsDiet(string question)
    {
        return ClearDiet.IsMatch(question);
    }

    public static string? RequestedPreference(string question)
    {
        if (ClearAllPreferences.IsMatch(question) || ClearTemperaturePreference.IsMatch(question))
        {
            return "none";
        }

        if (ClearLightPreference.IsMatch(question))
        {
            return null;
        }

        if (AvoidWarm.IsMatch(question) || (Cool.IsMatch(question) && !AvoidCool.IsMatch(question)))
        {
            return "cool";
        }

        if (AvoidCool.IsMatch(question) || Warm.IsMatch(question))
        {
            return "warm";
        }

        return Light.IsMatch(question) ? "light" : null;
    }

    public static bool AsksItemFacts(string question)
    {
        return PriceQuery.IsMatch(question) || AvailabilityQuery.IsMatch(question);
    }

    public static bool WantsSelection(string question)
    {
        return SelectionQuery.IsMatch(question);
    }

    public static bool AsksForAdditionalSuggestions(string question)
    {
        return AdditionalSelection.IsMatch(question);
    }

    public static bool AsksForAlternatives(string question)
    {
        return Alternatives.IsMatch(question);
    }

    public static bool WantsCheaper(string question)
    {
        return Cheaper.IsMatch(question);
    }

    public static bool ExcludesDishes(string question)
    {
        return Drop.IsMatch(question) && !Restore.IsMatch(question);
    }

    public static bool MatchesPreference(MenuItemDTO item, ChatContextDTO state)
    {
        if (state.RequiresLightMeal && !LightDescription.IsMatch(Fold(item.Description ?? "")))
        {
            return false;
        }

        return MatchesPreference(item, state.Preference);
    }

    public static bool IsExcluded(MenuItemDTO item, ChatContextDTO state)
    {
        return state.ExcludedItems.Contains(item.ItemId)
            || state.ExcludedTerms.Any(term => (" " + WordSequence(Fold(item.ItemName)) + " ")
                .Contains(" " + term + " ", StringComparison.Ordinal));
    }

    private static bool MatchesPreference(MenuItemDTO item, string preference)
    {
        var description = Fold(item.Description ?? "");
        return preference switch
        {
            "warm" => WarmDescription.IsMatch(description) && !CoolDescription.IsMatch(description)
                && !NegatedServingTemperature.IsMatch(description),
            "cool" => CoolDescription.IsMatch(description) && !WarmDescription.IsMatch(description)
                && !NegatedServingTemperature.IsMatch(description),
            "light" => LightDescription.IsMatch(description),
            _ => true
        };
    }

    public static bool WithinBudget(MenuItemDTO item, ChatContextDTO context, decimal? cheaperThan)
    {
        if (context.MaximumPrice is decimal limit && (item.Price is not decimal amount
            || amount > limit || (context.StrictPriceLimit && amount == limit)))
        {
            return false;
        }

        return cheaperThan is null || (item.Price is decimal price && price < cheaperThan);
    }

    public static ChatContextDTO FromHistory(IReadOnlyList<ConversationTurn> history, IReadOnlyList<MenuItemDTO> menu)
    {
        var context = new ChatContextDTO();
        foreach (var turn in history)
        {
            if (turn.Role == "assistant")
            {
                var prior = turn.Content.Split('\n')
                    .Where(line => PresentedLine.IsMatch(Fold(line.TrimStart())))
                    .Select(line => FindPresentedItem(line, menu) ?? Guid.Empty)
                    .Take(MaximumPriorItems)
                    .ToArray();
                if (prior.Length > 0)
                {
                    context = context with { PresentedItems = prior };
                }
            }
            else
            {
                var language = PreferredLanguage(turn.Content);
                if (language.Length > 0)
                {
                    context = context with { Language = language };
                }

                var question = Fold(turn.Content);
                var (refs, _) = References(question, NamedItems(question, menu), context.PresentedItems);
                context = Update(context, question, refs);
            }
        }

        return context;
    }

    public static IReadOnlyList<Guid> NamedItems(string question, IReadOnlyList<MenuItemDTO> menu)
    {
        var words = " " + WordSequence(question) + " ";
        return menu.Where(item =>
            {
                var name = WordSequence(Fold(item.ItemName));
                return name.Length > 0 && words.Contains(" " + name + " ", StringComparison.Ordinal);
            })
            .Select(item => item.ItemId).Distinct().ToArray();
    }

    public static (IReadOnlyList<Guid> Items, bool Ambiguous) References(
        string question, IReadOnlyList<Guid> named, IReadOnlyList<Guid> previous)
    {
        var positions = Ordinal.Matches(question);
        if (positions.Count > 0)
        {
            var selected = new List<Guid>();
            foreach (Match match in positions)
            {
                var position = match.Groups["vi"].Value + match.Groups["en"].Value;
                var index = position switch
                {
                    "2" or "hai" or "second" => 1,
                    "3" or "ba" or "third" => 2,
                    "cuoi" or "last" => previous.Count - 1,
                    "1" or "mot" or "nhat" or "first" => 0,
                    _ => -1
                };
                if (index < 0 || index >= previous.Count || previous[index] == Guid.Empty)
                {
                    return ([], true);
                }

                selected.Add(previous[index]);
            }

            return (selected.Distinct().ToArray(), false);
        }

        if (named.Count > 0)
        {
            return (named, false);
        }

        if (MultipleReference.IsMatch(question))
        {
            return previous.Count > 1 && !previous.Contains(Guid.Empty) ? (previous, false) : ([], true);
        }

        if (SingularReference.IsMatch(question) || (previous.Count > 0 && ShortReference.IsMatch(question)))
        {
            return previous.Count == 1 && previous[0] != Guid.Empty ? (previous, false) : ([], true);
        }

        return ([], false);
    }

    public static ChatContextDTO Update(ChatContextDTO state, string question, IReadOnlyList<Guid> references)
    {
        var excludedTerms = state.ExcludedTerms;
        var term = DishGroup.Match(question).Groups["name"].Value.Trim();
        if (term.Length > 0 && term.Length <= 80 && !Ordinal.IsMatch("mon " + term)
            && !SingularReference.IsMatch("mon " + term)
            && term is not ("mon" or "do" or "khac" or "ra" or "no" or "it" or "that" or "this"))
        {
            excludedTerms = Restore.IsMatch(question)
                ? excludedTerms.Where(value => value != term).ToArray()
                : excludedTerms.Append(term).Distinct().TakeLast(12).ToArray();
        }

        var excluded = state.ExcludedItems;
        if (Restore.IsMatch(question))
        {
            excluded = excluded.Except(references).ToArray();
        }
        else if (ExcludesDishes(question))
        {
            excluded = excluded.Concat(references).Distinct().TakeLast(MaximumExclusions).ToArray();
        }

        var maximum = state.MaximumPrice;
        var strict = state.StrictPriceLimit;
        var budget = Budget.Matches(question).Cast<Match>().LastOrDefault();
        if (budget is not null && decimal.TryParse(budget.Groups["amount"].Value.Replace(".", "").Replace(",", ""),
            NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
        {
            var scale = budget.Groups["scale"].Success ? 1000 : 1;
            if (amount <= decimal.MaxValue / scale)
            {
                maximum = amount * scale;
                strict = budget.Groups["op"].Value is "duoi" or "under" or "below" or "less than";
            }
        }
        else if (ClearBudget.IsMatch(question))
        {
            maximum = null;
            strict = false;
        }

        var preference = RequestedPreference(question) ?? state.Preference;
        var lightMeal = state.RequiresLightMeal || state.Preference == "light" || Light.IsMatch(question);
        if (preference == "light" && state.Preference is "warm" or "cool")
        {
            preference = state.Preference;
        }

        if (ClearAllPreferences.IsMatch(question) || ClearLightPreference.IsMatch(question))
        {
            lightMeal = false;
            if (preference == "light")
            {
                preference = "none";
            }
        }

        return state with
        {
            ExcludedTerms = excludedTerms,
            RequiresLightMeal = lightMeal,
            ExcludedItems = excluded,
            MaximumPrice = maximum,
            StrictPriceLimit = strict,
            Preference = preference,
            HasDietaryRestriction = !ClearDiet.IsMatch(question) && (state.HasDietaryRestriction || DietaryRestriction.IsMatch(question)),
            HasAllergyConcern = state.HasAllergyConcern || Allergy.IsMatch(question)
        };
    }

    public static string Fold(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character == 'đ' ? 'd' : character);
            }
        }

        return builder.ToString();
    }

    private static string WordSequence(string value)
    {
        return string.Join(" ", Regex.Matches(value, @"[\p{L}\p{N}]+").Select(match => match.Value));
    }

    private static Guid? FindPresentedItem(string line, IReadOnlyList<MenuItemDTO> menu)
    {
        var matches = menu.Where(item => line.TrimStart()
            .StartsWith(ChatText.ItemLabel(item.ItemName) + ":", StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0].ItemId,
            _ => Guid.Empty
        };
    }

    private static Regex Pattern(string pattern)
    {
        return new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }
}
