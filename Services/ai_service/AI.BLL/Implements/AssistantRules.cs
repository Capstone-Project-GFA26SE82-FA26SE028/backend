using System.Globalization;
using System.Text.RegularExpressions;
using AI.Common.DTOs;
using AI.Common.Exceptions;

namespace AI.BLL.Implements;

public static class AssistantRules
{
    public const string MissingFoodInformation = "Verified dietary, ingredient, allergen and spice information is not available. Please ask a staff member for clarification before ordering.";
    public const string MissingRestaurantInformation = "Verified restaurant contact, opening-hour and service-policy information is not available to this assistant. Please ask a staff member for clarification.";
    private static readonly Regex FoodInformation = new(
        @"\b(allerg\w*|vegan|vegetarian|halal|kosher|gluten|spicy|spice|mild|ingredient\w*|peanuts?|cashews?|contains? shellfish|nut.free|dairy.free|dị ứng|đậu phộng|lạc|hạt điều|có hải sản không|ăn chay|món chay|thuần chay|thành phần|nguyên liệu|độ cay|ít cay|không cay|di\s+ung|an\s+chay|thuan\s+chay|thanh\s+phan|nguyen\s+lieu|it\s+cay|khong\s+cay)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex PriceQuestion = new(
        @"\b(price|costs?|how much|giá|bao nhiêu tiền)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex AvailabilityQuestion = new(
        @"\b(available|orderable|in stock|out of stock|(?:can|could|may)\s+(?:i|we)\s+(?:still\s+)?order|đặt được|có thể đặt|gọi được|còn hàng|hết hàng|còn bán|hết món|còn món)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex BudgetPattern = new(
        @"\b(?:under|below|less than|up to|at most|budget(?: of| is)?|dưới|tối đa|không quá|ngân sách(?: là)?)\s*[:=]?\s*(?:(?<grouped>\d{1,3}(?:[.,]\d{3})+)|(?<amount>\d+(?:\.\d{1,2})?))\s*(?<scale>k|thousand|nghìn|ngàn)?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static void ValidateGuest(GuestContext guest)
    {
        if (guest.TableSessionId == Guid.Empty || guest.Token.Length != 64 || !guest.Token.All(Uri.IsHexDigit))
        {
            throw new AIException(401, "A valid QR guest session is required.");
        }
    }

    public static bool NeedsVerifiedFoodInformation(string message)
    {
        return FoodInformation.IsMatch(message);
    }

    public static string ResolveMenuTopic(string message, string modelTopic)
    {
        if (PriceQuestion.IsMatch(message) && AvailabilityQuestion.IsMatch(message))
        {
            return "overview";
        }

        return modelTopic;
    }

    public static bool CanRecommend(MenuItemDTO item)
    {
        return item.CanOrder && item.Price is >= 0 && item.PriceId.HasValue && item.PriceId != Guid.Empty;
    }

    public static void ValidateCart(CartContextDTO cart, RecommendationRequestDTO request, DateTimeOffset now)
    {
        if (cart.CartId != request.CartId || cart.TableSessionId != request.Guest.TableSessionId)
        {
            throw new AIException(503, "The cart response does not match the requested guest session.");
        }

        if (cart.ExpiresAt <= now)
        {
            throw new AIException(409, "The cart has expired. Please refresh your cart.");
        }

        if (string.IsNullOrEmpty(cart.Version) || cart.Items.Any(line => line.ItemId == Guid.Empty || line.Quantity <= 0))
        {
            throw new AIException(503, "The cart response is invalid.");
        }
    }

    public static IReadOnlyDictionary<string, MenuItemDTO> Keyed(IReadOnlyList<MenuItemDTO> items)
    {
        return items.Select((item, index) => new { Key = $"m{index}", Item = item })
            .ToDictionary(value => value.Key, value => value.Item, StringComparer.Ordinal);
    }

    public static IReadOnlyList<ModelItemDTO> ModelItems(IReadOnlyDictionary<string, MenuItemDTO> items)
    {
        return items.Select(pair => new ModelItemDTO(pair.Key,
            Shorten(pair.Value.ItemName, 80), Shorten(pair.Value.CategoryName, 60),
            pair.Value.Description is null ? null : Shorten(pair.Value.Description, 100),
            pair.Value.Price, CanRecommend(pair.Value))).ToArray();
    }

    public static decimal? NumericBudget(string message)
    {
        var match = BudgetPattern.Match(message);
        var grouped = match.Groups["grouped"];
        var value = grouped.Success ? grouped.Value.Replace(",", "").Replace(".", "") : match.Groups["amount"].Value;
        if (!match.Success || !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
        {
            return null;
        }

        if (match.Groups["scale"].Success)
        {
            return amount <= decimal.MaxValue / 1000 ? amount * 1000 : null;
        }

        return amount;
    }

    private static string Shorten(string text, int maximum)
    {
        return text[..Math.Min(text.Length, maximum)];
    }
}
