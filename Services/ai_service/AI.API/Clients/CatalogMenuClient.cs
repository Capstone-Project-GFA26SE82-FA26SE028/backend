using System.Globalization;
using AI.BLL.Interfaces;
using AI.Common.DTOs;
using AI.Common.Exceptions;
using AI.Common.Settings;
using Backend.Contracts.Catalog;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace AI.API.Clients;

public sealed class CatalogMenuClient(MenuGrpc.MenuGrpcClient client, IOptions<AISettings> settings) : ICatalogMenuClient
{
    public async Task<MenuPageDTO> GetAllAsync(GuestContext guest, string channel, int page, int pageSize, CancellationToken ct)
    {
        var reply = await client.GetAllAsync(new MenuListRequest
        {
            Access = Access(guest),
            Channel = channel,
            Page = page,
            PageSize = pageSize
        }, deadline: Deadline(), cancellationToken: ct);
        RequireSuccess(reply.IsSuccess, reply.StatusCode);
        return new MenuPageDTO(reply.Items.Select(item => Map(item, channel)).ToArray(), reply.TotalCount);
    }

    public async Task<MenuItemDTO?> GetByIdAsync(GuestContext guest, string channel, Guid itemId, CancellationToken ct)
    {
        try
        {
            var reply = await client.GetByIdAsync(new MenuItemRequest
            {
                Access = Access(guest),
                Channel = channel,
                ItemId = itemId.ToString()
            }, deadline: Deadline(), cancellationToken: ct);
            if (reply.StatusCode == 404)
            {
                return null;
            }

            RequireSuccess(reply.IsSuccess, reply.StatusCode);
            if (reply.Result is null)
            {
                throw new AIException(503, "The public menu item response is missing.");
            }

            var item = Map(reply.Result, channel);
            if (item.ItemId != itemId)
            {
                throw new AIException(503, "The public menu returned an unexpected item ID.");
            }

            return item;
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    private DateTime Deadline()
    {
        return DateTime.UtcNow.AddSeconds(settings.Value.DependencyTimeoutSeconds);
    }

    private static void RequireSuccess(bool success, int statusCode)
    {
        if (!success)
        {
            throw new AIException(statusCode is 400 or 401 or 403 ? statusCode : 503, "The public menu could not be retrieved.");
        }
    }

    private static MenuAccessRequest Access(GuestContext guest)
    {
        return new MenuAccessRequest
        {
            TableSessionId = guest.TableSessionId.ToString(),
            GuestToken = guest.Token
        };
    }

    private static MenuItemDTO Map(MenuItemInfo item, string channel)
    {
        if (!Guid.TryParse(item.ItemId, out var id) || id == Guid.Empty
            || !Guid.TryParse(item.CategoryId, out var categoryId) || categoryId == Guid.Empty
            || string.IsNullOrWhiteSpace(item.ItemName) || item.Channel != channel
            || !DateTimeOffset.TryParse(item.EvaluatedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
        {
            throw new AIException(503, "The public menu item data is invalid.");
        }

        decimal? amount = null;
        Guid? priceId = null;
        if (item.HasPrice)
        {
            if (!decimal.TryParse(item.Price, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var price) || price < 0)
            {
                throw new AIException(503, "The public menu price is invalid.");
            }

            amount = price;
        }

        if (item.HasPriceId)
        {
            if (!Guid.TryParse(item.PriceId, out var parsed) || parsed == Guid.Empty)
            {
                throw new AIException(503, "The public menu price reference is invalid.");
            }

            priceId = parsed;
        }

        if (item.CanOrder && (!amount.HasValue || !priceId.HasValue))
        {
            throw new AIException(503, "An orderable public menu item must have a verified price reference.");
        }

        return new MenuItemDTO
        {
            ItemId = id,
            CategoryId = categoryId,
            CategoryName = item.CategoryName,
            ItemName = item.ItemName,
            Description = item.HasDescription ? item.Description : null,
            ImageUrl = item.HasImageUrl ? item.ImageUrl : null,
            Price = amount,
            PriceId = priceId,
            Availability = item.Availability,
            CanOrder = item.CanOrder,
            UnavailableReason = item.HasUnavailableReason ? item.UnavailableReason : null,
            Channel = channel,
            EvaluatedAt = at,
            RequiresOptionSelection = item.OptionGroups.Any(group => group.MinSelection > 0)
        };
    }
}
