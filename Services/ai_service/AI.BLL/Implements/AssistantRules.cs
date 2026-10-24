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
        @"\b(allerg\w*|vegan|vegetarian|halal|kosher|gluten|spicy|spice|ingredient\w*|nut.free|dairy.free)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex BudgetPattern = new(
        @"\b(?:under|below|less than|up to|at most|budget(?: of| is)?)\s*(?<amount>(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d{1,2})?)\s*(?<scale>k|thousand)?\b",
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
        if (!match.Success || !decimal.TryParse(match.Groups["amount"].Value,
            NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
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
