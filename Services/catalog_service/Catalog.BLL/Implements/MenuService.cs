using Catalog.BLL.Interfaces;
using Catalog.Common.DTOs;
using Catalog.Common.Settings;
using Catalog.DAL.Interfaces;
using Catalog.DAL.Models;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;
using Shared.Common.Pagination;

namespace Catalog.BLL.Implements;

public sealed class MenuService(
    IItemRepository items,
    IPriceRepository prices,
    IMenuAccess access,
    IOptions<PricingSettings> settings,
    TimeProvider clock) : IMenuService
{
    public async Task<ResponseDTO> GetAll(
        MenuQueryDTO query, Guid? tableSessionId, string? guestToken, CancellationToken cancellationToken)
    {
        await access.RequireReadAsync(tableSessionId, guestToken, cancellationToken);
        if (query.Page < 1 || query.PageSize is < 1 or > 100
            || (long)(query.Page - 1) * query.PageSize > int.MaxValue
            || query.CategoryId == Guid.Empty || query.Search?.Length > 255)
        {
            return new ResponseDTO("Invalid category, search (maximum 255 characters), or pagination (page >= 1, pageSize 1-100).", 400);
        }

        var error = ValidateChannel(query.Channel, out var timeZone);
        if (error is not null)
        {
            return error;
        }

        var now = prices.NormalizeTimestamp(clock.GetUtcNow().UtcDateTime);
        var page = await items.GetMenuPageAsync(query, now, cancellationToken);
        return new ResponseDTO("Menu retrieved successfully.", 200, true, new PagedResult<MenuItemDTO>(
            page.Items.Select(item => Map(item, query.Channel, now, timeZone)).ToArray(),
            page.TotalCount, page.Page, page.PageSize));
    }

    public async Task<ResponseDTO> GetById(
        Guid id, string channel, Guid? tableSessionId, string? guestToken, CancellationToken cancellationToken)
    {
        await access.RequireReadAsync(tableSessionId, guestToken, cancellationToken);
        if (id == Guid.Empty)
        {
            return new ResponseDTO("Invalid item ID.", 400);
        }

        var error = ValidateChannel(channel, out var timeZone);
        if (error is not null)
        {
            return error;
        }

        var now = prices.NormalizeTimestamp(clock.GetUtcNow().UtcDateTime);
        var item = await items.GetMenuByIdAsync(id, now, cancellationToken);
        if (item is null)
        {
            return new ResponseDTO("Menu item not found.", 404);
        }

        return new ResponseDTO("Menu item retrieved successfully.", 200, true, Map(item, channel, now, timeZone));
    }

    private ResponseDTO? ValidateChannel(string channel, out TimeZoneInfo timeZone)
    {
        if (!PriceRules.TryConfiguration(settings.Value, out timeZone))
        {
            return new ResponseDTO("Menu pricing is not configured.", 503);
        }

        if (!settings.Value.Channels.Contains(channel, StringComparer.Ordinal))
        {
            return new ResponseDTO("A configured sales channel is required.", 400);
        }

        return null;
    }

    private MenuItemDTO Map(Item item, string channel, DateTime now, TimeZoneInfo timeZone)
    {
        var selected = PriceRules.ResolveForSale(item.Prices, channel, settings.Value, now, timeZone);
        var validPrice = selected is not null;
        var groups = item.ItemOptionGroups.OrderBy(link => link.OptionGroup.OptionGroupName)
            .ThenBy(link => link.OptionGroupId).Select(link => new MenuOptionGroupDTO
            {
                OptionGroupId = link.OptionGroupId,
                OptionGroupName = link.OptionGroup.OptionGroupName,
                MinSelection = link.MinSelection,
                MaxSelection = link.MaxSelection,
                Options = link.OptionGroup.Options
                    .Where(option => option.Availability is ItemAvailability.Available or ItemAvailability.OutOfStock)
                    .OrderBy(option => option.OptionName).ThenBy(option => option.OptionId)
                    .Select(option => new MenuOptionDTO
                    {
                        OptionId = option.OptionId,
                        OptionName = option.OptionName,
                        ExtraPrice = option.ExtraPrice,
                        Availability = option.Availability,
                        CanSelect = option.Availability == ItemAvailability.Available && option.ExtraPrice >= 0
                    }).ToList()
            }).ToList();
        string? reason = null;
        if (item.Availability != ItemAvailability.Available)
        {
            reason = "OutOfStock";
        }
        else if (!validPrice)
        {
            reason = "PriceUnavailable";
        }
        else if (item.RecipeVersions.Count != 1)
        {
            reason = "RecipeUnavailable";
        }
        else if (groups.Any(group => group.MinSelection > 0 && !group.Options.Any(option => option.CanSelect)))
        {
            reason = "RequiredOptionsUnavailable";
        }

        return new MenuItemDTO
        {
            ItemId = item.ItemId,
            CategoryId = item.CategoryId,
            CategoryName = item.Category.CategoryName,
            ItemCode = item.ItemCode,
            ItemName = item.ItemName,
            Description = item.Description,
            ImageUrl = item.ImageUrl,
            Availability = item.Availability,
            Price = selected?.Amount,
            PriceId = selected?.PriceId,
            Channel = channel,
            EvaluatedAt = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)),
            CanOrder = reason is null,
            UnavailableReason = reason,
            OptionGroups = groups
        };
    }
}
