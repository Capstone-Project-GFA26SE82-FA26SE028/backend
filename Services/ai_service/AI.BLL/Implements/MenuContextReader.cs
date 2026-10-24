using System.Text.RegularExpressions;
using AI.BLL.Interfaces;
using AI.Common.DTOs;
using AI.Common.Exceptions;
using AI.Common.Settings;
using Microsoft.Extensions.Options;

namespace AI.BLL.Implements;

public sealed class MenuContextReader(ICatalogMenuClient catalog, IOptions<AISettings> options)
{
    public async Task<MenuPageDTO> Read(GuestContext guest, CancellationToken ct)
    {
        var settings = options.Value;
        var items = new List<MenuItemDTO>();
        var total = 0;
        for (var page = 1; items.Count < settings.MaximumMenuItems; page++)
        {
            var result = await catalog.GetAllAsync(guest, settings.Channel, page, settings.MenuPageSize, ct);
            total = result.TotalCount;
            if (total < 0 || result.Items.Any(item => item.ItemId == Guid.Empty || item.Channel != settings.Channel))
            {
                throw new AIException(503, "The public menu response is invalid.");
            }

            items.AddRange(result.Items.Take(settings.MaximumMenuItems - items.Count));
            if (result.Items.Count == 0 || items.Count >= total)
            {
                break;
            }
        }

        return new MenuPageDTO(items.DistinctBy(item => item.ItemId).ToArray(), total);
    }

    public IReadOnlyList<MenuItemDTO> Candidates(IEnumerable<MenuItemDTO> menu, string query)
    {
        var words = Regex.Matches(query, @"[\p{L}\p{N}]+")
            .Select(match => match.Value).Where(word => word.Length > 2).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return menu.OrderByDescending(item => words.Sum(word =>
                (item.ItemName.Contains(word, StringComparison.OrdinalIgnoreCase) ? 6 : 0)
                + (item.CategoryName.Contains(word, StringComparison.OrdinalIgnoreCase) ? 3 : 0)
                + (item.Description?.Contains(word, StringComparison.OrdinalIgnoreCase) == true ? 1 : 0)))
            .ThenBy(item => item.ItemId).Take(options.Value.ModelCandidateCount).ToArray();
    }

    public async Task<MenuItemDTO?> Refresh(GuestContext guest, Guid itemId, CancellationToken ct)
    {
        var item = await catalog.GetByIdAsync(guest, options.Value.Channel, itemId, ct);
        if (item is not null && (item.ItemId != itemId || item.Channel != options.Value.Channel))
        {
            throw new AIException(503, "The refreshed menu response does not match the requested item.");
        }

        return item;
    }
}
