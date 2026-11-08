using System.Globalization;
using AI.Common.DTOs;
using Backend.Contracts.AI;

namespace AI.API.GrpcServices;

public static class AIGrpcMapping
{
    public static GuestContext Guest(GuestAccess? access)
    {
        return new GuestContext(Guid.TryParse(access?.TableSessionId, out var id) ? id : Guid.Empty, access?.GuestToken ?? string.Empty);
    }

    public static MenuCard Card(MenuItemDTO item)
    {
        var card = new MenuCard
        {
            ItemId = item.ItemId.ToString(),
            CategoryId = item.CategoryId.ToString(),
            ItemName = item.ItemName,
            CategoryName = item.CategoryName,
            Availability = item.Availability,
            CanOrder = item.CanOrder,
            Channel = item.Channel,
            EvaluatedAt = item.EvaluatedAt.ToString("O"),
            RequiresOptionSelection = item.RequiresOptionSelection
        };
        if (item.Description is not null)
        {
            card.Description = item.Description;
        }

        if (item.ImageUrl is not null)
        {
            card.ImageUrl = item.ImageUrl;
        }

        if (item.Price.HasValue)
        {
            card.Price = item.Price.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (item.PriceId.HasValue)
        {
            card.PriceId = item.PriceId.Value.ToString();
        }

        if (item.UnavailableReason is not null)
        {
            card.UnavailableReason = item.UnavailableReason;
        }

        return card;
    }
}
