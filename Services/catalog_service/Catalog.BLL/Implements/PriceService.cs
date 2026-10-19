using Catalog.BLL.Interfaces;
using Catalog.Common.DTOs;
using Catalog.Common.Exceptions;
using Catalog.Common.Settings;
using Catalog.DAL.Interfaces;
using Catalog.DAL.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;
using Shared.Common.Pagination;

namespace Catalog.BLL.Implements;

public sealed class PriceService(
    IPriceRepository prices,
    IItemRepository items,
    IPriceTransaction transaction,
    ICatalogAccess access,
    IOptions<PricingSettings> settings,
    TimeProvider timeProvider,
    ILogger<PriceService> logger) : IPriceService
{
    public async Task<ResponseDTO> GetAll(PriceQueryDTO query, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (query is null || !query.IsValid())
        {
            return new ResponseDTO("Invalid pagination, item ID, or channel filter.", 400);
        }

        if (query.ItemId.HasValue && await items.GetByIdAsync(query.ItemId.Value, cancellationToken) is null)
        {
            return ItemNotFound();
        }

        var page = await prices.GetPageAsync(query, cancellationToken);
        return new ResponseDTO("Prices retrieved successfully.", 200, true,
            new PagedResult<PriceDTO>(page.Items.Select(Map).ToArray(), page.TotalCount, page.Page, page.PageSize));
    }

    public async Task<ResponseDTO> GetById(Guid id, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (id == Guid.Empty)
        {
            return InvalidId();
        }

        var price = await prices.GetByIdAsync(id, cancellationToken);
        return price is null ? NotFound() : new ResponseDTO("Price retrieved successfully.", 200, true, Map(price));
    }

    public async Task<ResponseDTO> Create(CreatePriceDTO request, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (request is null || request.ItemId == Guid.Empty)
        {
            return new ResponseDTO("A valid item ID is required.", 400);
        }

        if (!TryConfiguration(out var timeZone))
        {
            return ConfigurationMissing();
        }

        var validation = ValidateTerms(request.Channel, request.Amount, request.EffectiveFrom,
            request.EffectiveTo, request.DailyStart, request.DailyEnd, out var candidate);
        if (validation is not null)
        {
            return validation;
        }

        candidate.ItemId = request.ItemId;
        candidate.PriceId = Guid.NewGuid();
        return await WriteAsync(request.ItemId, async () =>
        {
            if (await items.GetByIdAsync(request.ItemId, cancellationToken) is null)
            {
                return (ItemNotFound(), null);
            }

            if (await HasOverlap(candidate, Guid.Empty, timeZone, cancellationToken))
            {
                return (Overlap(), null);
            }

            prices.Create(candidate);
            return (new ResponseDTO("Price created successfully.", 201, true), candidate);
        }, cancellationToken);
    }

    public async Task<ResponseDTO> Update(Guid id, UpdatePriceDTO request, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (id == Guid.Empty)
        {
            return InvalidId();
        }

        if (request is null)
        {
            return new ResponseDTO("A price request is required.", 400);
        }

        if (!TryConfiguration(out var timeZone))
        {
            return ConfigurationMissing();
        }

        var validation = ValidateTerms(request.Channel, request.Amount, request.EffectiveFrom,
            request.EffectiveTo, request.DailyStart, request.DailyEnd, out var candidate);
        if (validation is not null)
        {
            return validation;
        }

        return await WriteExistingAsync(id, async current =>
        {
            var now = Now();
            if (current.EffectiveFrom <= now)
            {
                return (new ResponseDTO("Only prices that have not started can be updated. Replace an effective price instead.", 409), null);
            }

            if (candidate.EffectiveFrom <= now)
            {
                return (new ResponseDTO("An updated future price must still start in the future.", 400), null);
            }

            candidate.ItemId = current.ItemId;
            if (await HasOverlap(candidate, id, timeZone, cancellationToken))
            {
                return (Overlap(), null);
            }

            current.Channel = candidate.Channel;
            current.Amount = candidate.Amount;
            current.EffectiveFrom = candidate.EffectiveFrom;
            current.EffectiveTo = candidate.EffectiveTo;
            current.DailyStart = candidate.DailyStart;
            current.DailyEnd = candidate.DailyEnd;
            return (new ResponseDTO("Price updated successfully.", 200, true), current);
        }, cancellationToken);
    }

    public async Task<ResponseDTO> Delete(Guid id, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (id == Guid.Empty)
        {
            return InvalidId();
        }

        return await WriteExistingAsync(id, current =>
        {
            if (current.EffectiveFrom <= Now())
            {
                return Task.FromResult<(ResponseDTO, Price?)>((
                    new ResponseDTO("Only prices that have not started can be deleted.", 409), null));
            }

            prices.Delete(current);
            return Task.FromResult<(ResponseDTO, Price?)>((new ResponseDTO("Price deleted successfully.", 200, true), null));
        }, cancellationToken);
    }

    public async Task<ResponseDTO> Replace(Guid id, ReplacePriceDTO request, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (id == Guid.Empty)
        {
            return InvalidId();
        }

        if (request is null)
        {
            return new ResponseDTO("A price request is required.", 400);
        }

        if (!TryConfiguration(out var timeZone))
        {
            return ConfigurationMissing();
        }

        if (!PriceRules.TryAmount(request.Amount, out var amount))
        {
            return InvalidAmount();
        }

        DateTime? end = null;
        if (request.EffectiveTo is not null)
        {
            if (!PriceRules.TryUtc(request.EffectiveTo, out var effectiveTo))
            {
                return InvalidTimestamp();
            }

            end = prices.NormalizeTimestamp(effectiveTo);
        }

        return await WriteExistingAsync(id, async current =>
        {
            var transitionAt = Now();
            if (current.EffectiveFrom >= transitionAt
                || (current.EffectiveTo.HasValue && transitionAt >= current.EffectiveTo.Value))
            {
                return (new ResponseDTO("Only an effective price with a non-empty elapsed interval can be replaced.", 409), null);
            }

            if (end.HasValue && end.Value <= transitionAt)
            {
                return (new ResponseDTO("The new end time must be after the server transition time.", 400), null);
            }

            if ((current.Channel != settings.Value.AllChannelsCode
                    && !settings.Value.Channels.Contains(current.Channel, StringComparer.Ordinal))
                || current.DailyStart.HasValue != current.DailyEnd.HasValue
                || (current.DailyStart.HasValue && current.DailyEnd <= current.DailyStart))
            {
                return (new ResponseDTO("The existing price has an unsupported channel or daily window. Resolve its configuration before replacement.", 409), null);
            }

            var replacement = new Price
            {
                PriceId = Guid.NewGuid(),
                ItemId = current.ItemId,
                Channel = current.Channel,
                Amount = amount,
                EffectiveFrom = transitionAt,
                EffectiveTo = end,
                DailyStart = current.DailyStart,
                DailyEnd = current.DailyEnd
            };
            if (await HasOverlap(replacement, id, timeZone, cancellationToken))
            {
                return (Overlap(), null);
            }

            current.EffectiveTo = transitionAt;
            prices.Create(replacement);
            return (new ResponseDTO("Price replaced successfully.", 201, true), replacement);
        }, cancellationToken);
    }

    public async Task<ResponseDTO> GetAppliedPrice(
        Guid itemId, string? channel, string? at, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (itemId == Guid.Empty)
        {
            return new ResponseDTO("Invalid item ID.", 400);
        }

        if (!TryConfiguration(out var timeZone))
        {
            return ConfigurationMissing();
        }

        if (!settings.Value.Channels.Contains(channel, StringComparer.Ordinal))
        {
            return new ResponseDTO("A configured sales channel is required.", 400);
        }

        var instant = Now();
        if (at is not null)
        {
            if (!PriceRules.TryUtc(at, out instant))
            {
                return InvalidTimestamp();
            }

            instant = prices.NormalizeTimestamp(instant);
        }

        if (await items.GetByIdAsync(itemId, cancellationToken) is null)
        {
            return ItemNotFound();
        }

        var candidates = (await prices.GetForItemAsync(itemId, cancellationToken))
            .Where(price => (price.Channel == channel || price.Channel == settings.Value.AllChannelsCode)
                && PriceRules.AppliesAt(price, instant, timeZone)).ToArray();
        if (candidates.Length == 0)
        {
            return new ResponseDTO("No applicable price was found for this item, channel, and time.", 404);
        }

        var priority = candidates.Max(price => Priority(price, channel!));
        var selected = candidates.Where(price => Priority(price, channel!) == priority).ToArray();
        if (selected.Length != 1)
        {
            return new ResponseDTO("Multiple prices have the same applicable priority. Resolve the conflicting schedules.", 409);
        }

        var result = selected[0];
        return new ResponseDTO("Applicable price retrieved successfully.", 200, true, new AppliedPriceDTO
        {
            ItemId = itemId,
            PriceId = result.PriceId,
            Amount = result.Amount,
            RequestedChannel = channel!,
            PriceChannel = result.Channel,
            EvaluatedAt = Utc(instant),
            HasValidStandardPrice = candidates.Count(price => price.Channel == settings.Value.AllChannelsCode
                && !price.DailyStart.HasValue && !price.DailyEnd.HasValue) == 1,
            Price = Map(result)
        });
    }

    private async Task<ResponseDTO> WriteExistingAsync(
        Guid id, Func<Price, Task<(ResponseDTO Response, Price? Price)>> action, CancellationToken cancellationToken)
    {
        var itemId = await prices.GetItemIdAsync(id, cancellationToken);
        if (!itemId.HasValue)
        {
            return NotFound();
        }

        return await WriteAsync(itemId.Value, async () =>
        {
            var current = await prices.GetByIdAsync(id, cancellationToken);
            if (current is null || current.ItemId != itemId.Value)
            {
                return (NotFound(), null);
            }

            return await action(current);
        }, cancellationToken);
    }

    private async Task<ResponseDTO> WriteAsync(
        Guid itemId, Func<Task<(ResponseDTO Response, Price? Price)>> action, CancellationToken cancellationToken)
    {
        try
        {
            var result = await transaction.ExecuteAsync(itemId, action, cancellationToken);
            if (result.Price is not null)
            {
                result.Response.Result = Map(result.Price);
            }

            return result.Response;
        }
        catch (PricePersistenceException exception)
        {
            logger.LogWarning(exception, "Price persistence failed: {Failure}", exception.Failure);
            return exception.Failure switch
            {
                PricePersistenceFailure.Conflict => Stale(),
                PricePersistenceFailure.RelatedDataChanged => new ResponseDTO("Related data has changed. Reload and try again.", 409),
                PricePersistenceFailure.Busy => new ResponseDTO("A concurrent operation was detected. Try again.", 409),
                _ => new ResponseDTO("The price data store is unavailable.", 503)
            };
        }
    }

    private ResponseDTO? ValidateTerms(
        string? channel, string? amount, string? from, string? to,
        string? dailyStart, string? dailyEnd, out Price price)
    {
        price = new Price();
        if (channel != settings.Value.AllChannelsCode
            && !settings.Value.Channels.Contains(channel, StringComparer.Ordinal))
        {
            return new ResponseDTO("The channel is not configured for pricing.", 400);
        }

        if (!PriceRules.TryAmount(amount, out var value))
        {
            return InvalidAmount();
        }

        if (!PriceRules.TryUtc(from, out var start))
        {
            return InvalidTimestamp();
        }

        start = prices.NormalizeTimestamp(start);
        DateTime? end = null;
        if (to is not null)
        {
            if (!PriceRules.TryUtc(to, out var parsedEnd))
            {
                return InvalidTimestamp();
            }

            end = prices.NormalizeTimestamp(parsedEnd);
            if (end.Value <= start)
            {
                return new ResponseDTO("EffectiveTo must be after EffectiveFrom at database timestamp precision.", 400);
            }
        }

        if (!PriceRules.TryWindow(dailyStart, dailyEnd, out var windowStart, out var windowEnd))
        {
            return new ResponseDTO("Provide both daily times as HH:mm or HH:mm:ss, with the end after the start. Overnight windows are not supported.", 400);
        }

        price.Channel = channel!;
        price.Amount = value;
        price.EffectiveFrom = start;
        price.EffectiveTo = end;
        price.DailyStart = windowStart;
        price.DailyEnd = windowEnd;
        return null;
    }

    private async Task<bool> HasOverlap(Price candidate, Guid excludedId, TimeZoneInfo timeZone, CancellationToken cancellationToken)
    {
        var existing = await prices.GetForItemAsync(candidate.ItemId, cancellationToken);
        return existing.Any(price => price.PriceId != excludedId && PriceRules.Overlaps(candidate, price, timeZone));
    }

    private bool TryConfiguration(out TimeZoneInfo timeZone)
    {
        timeZone = TimeZoneInfo.Utc;
        var configuration = settings.Value;
        if (!ValidChannel(configuration.AllChannelsCode) || configuration.Channels is not { Length: > 0 }
            || !configuration.Channels.All(ValidChannel)
            || configuration.Channels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != configuration.Channels.Length
            || configuration.Channels.Contains(configuration.AllChannelsCode, StringComparer.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(configuration.BusinessTimeZone))
        {
            return false;
        }

        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(configuration.BusinessTimeZone);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    private static bool ValidChannel(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Length <= 50
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    }

    private DateTime Now()
    {
        return prices.NormalizeTimestamp(timeProvider.GetUtcNow().UtcDateTime);
    }

    private static int Priority(Price price, string channel)
    {
        return (price.DailyStart.HasValue ? 2 : 0) + (price.Channel == channel ? 1 : 0);
    }

    private static DateTimeOffset Utc(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private static PriceDTO Map(Price price)
    {
        return new PriceDTO
        {
            PriceId = price.PriceId,
            ItemId = price.ItemId,
            Channel = price.Channel,
            Amount = price.Amount,
            EffectiveFrom = Utc(price.EffectiveFrom),
            EffectiveTo = price.EffectiveTo.HasValue ? Utc(price.EffectiveTo.Value) : null,
            DailyStart = price.DailyStart,
            DailyEnd = price.DailyEnd
        };
    }

    private ResponseDTO ConfigurationMissing()
    {
        logger.LogError("Configure Pricing:AllChannelsCode, Channels, and BusinessTimeZone before managing prices.");
        return new ResponseDTO("Pricing is unavailable until its channels and business time zone are configured.", 503);
    }

    private static ResponseDTO InvalidAmount()
    {
        return new ResponseDTO("Amount must be non-negative, have at most two decimal places, and fit decimal(18,2).", 400);
    }

    private static ResponseDTO InvalidTimestamp()
    {
        return new ResponseDTO("Use an ISO 8601 timestamp with an explicit UTC offset within the SQL datetime range.", 400);
    }

    private static ResponseDTO InvalidId()
    {
        return new ResponseDTO("Invalid price ID.", 400);
    }

    private static ResponseDTO NotFound()
    {
        return new ResponseDTO("Price not found.", 404);
    }

    private static ResponseDTO ItemNotFound()
    {
        return new ResponseDTO("Item not found.", 404);
    }

    private static ResponseDTO Stale()
    {
        return new ResponseDTO("The price has changed. Reload and try again.", 409);
    }

    private static ResponseDTO Overlap()
    {
        return new ResponseDTO("Another price overlaps this scope and effective time.", 409);
    }
}
