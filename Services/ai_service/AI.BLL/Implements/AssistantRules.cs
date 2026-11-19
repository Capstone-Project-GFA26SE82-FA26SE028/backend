using AI.Common.DTOs;
using AI.Common.Exceptions;

namespace AI.BLL.Implements;

public static class AssistantRules
{
    public const string MissingFoodInformation = "Verified dietary, ingredient, allergen and spice information is not available. Please ask a staff member for clarification before ordering.";
    public const string MissingRestaurantInformation = "Verified restaurant contact, opening-hour and service-policy information is not available to this assistant. Please ask a staff member for clarification.";
    public static void ValidateGuest(GuestContext guest)
    {
        if (guest.TableSessionId == Guid.Empty || guest.Token.Length != 64 || !guest.Token.All(Uri.IsHexDigit))
        {
            throw new AIException(401, "A valid QR guest session is required.");
        }
    }

    public static bool CanRecommend(MenuItemDTO item)
    {
        return item.CanOrder && item.Price is >= 0 && item.PriceId.HasValue && item.PriceId != Guid.Empty;
    }

    public static void RequireSameGuest(VerifiedGuest initial, VerifiedGuest current)
    {
        if (initial.TableSessionId != current.TableSessionId || initial.GuestAccessId != current.GuestAccessId
            || initial.RestaurantId != current.RestaurantId)
        {
            throw new AIException(403, "The QR guest session changed. Please join the table again.");
        }
    }

    public static void ValidateCart(CartContextDTO cart, Guid cartId, VerifiedGuest guest, DateTimeOffset now)
    {
        if (cart.CartId != cartId || cart.TableSessionId != guest.TableSessionId || cart.RestaurantId != guest.RestaurantId)
        {
            throw new AIException(503, "The cart response does not match the requested guest session and restaurant.");
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

    private static string Shorten(string text, int maximum)
    {
        return text[..Math.Min(text.Length, maximum)];
    }
}
