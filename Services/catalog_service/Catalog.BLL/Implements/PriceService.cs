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
            return new ResponseDTO("Phân trang, mã món ăn hoặc kênh bán không hợp lệ.", 400);
        }

        if (query.ItemId.HasValue && await items.GetByIdAsync(query.ItemId.Value, cancellationToken) is null)
        {
            return ItemNotFound();
        }

        var page = await prices.GetPageAsync(query, cancellationToken);
        return new ResponseDTO("Lấy danh sách giá thành công.", 200, true,
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
        return price is null ? NotFound() : new ResponseDTO("Lấy thông tin giá thành công.", 200, true, Map(price));
    }

    public async Task<ResponseDTO> Create(CreatePriceDTO request, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (request is null || request.ItemId == Guid.Empty)
        {
            return new ResponseDTO("Yêu cầu mã món ăn hợp lệ.", 400);
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
            return (new ResponseDTO("Tạo giá thành công.", 201, true), candidate);
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
            return new ResponseDTO("Yêu cầu thông tin giá.", 400);
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
                return (new ResponseDTO("Chỉ có thể cập nhật giá chưa bắt đầu hiệu lực. Hãy thay thế nếu giá đang có hiệu lực.", 409), null);
            }

            if (candidate.EffectiveFrom <= now)
            {
                return (new ResponseDTO("Giá cập nhật trong tương lai vẫn phải bắt đầu ở thời điểm tương lai.", 400), null);
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
            return (new ResponseDTO("Cập nhật giá thành công.", 200, true), current);
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
                    new ResponseDTO("Chỉ có thể xóa giá chưa bắt đầu hiệu lực.", 409), null));
            }

            prices.Delete(current);
            return Task.FromResult<(ResponseDTO, Price?)>((new ResponseDTO("Xóa giá thành công.", 200, true), null));
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
            return new ResponseDTO("Yêu cầu thông tin giá.", 400);
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
                return (new ResponseDTO("Chỉ có thể thay thế giá đang có hiệu lực.", 409), null);
            }

            if (end.HasValue && end.Value <= transitionAt)
            {
                return (new ResponseDTO("Thời gian kết thúc mới phải sau thời điểm chuyển đổi của hệ thống.", 400), null);
            }

            if ((current.Channel != settings.Value.AllChannelsCode
                    && !settings.Value.Channels.Contains(current.Channel, StringComparer.Ordinal))
                || current.DailyStart.HasValue != current.DailyEnd.HasValue
                || (current.DailyStart.HasValue && current.DailyEnd <= current.DailyStart))
            {
                return (new ResponseDTO("Giá hiện tại có kênh bán hoặc khung giờ không được hỗ trợ. Hãy kiểm tra lại cấu hình.", 409), null);
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
            return (new ResponseDTO("Thay thế giá thành công.", 201, true), replacement);
        }, cancellationToken);
    }

    public async Task<ResponseDTO> GetAppliedPrice(
        Guid itemId, string? channel, string? at, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (itemId == Guid.Empty)
        {
            return new ResponseDTO("ID món ăn không hợp lệ.", 400);
        }

        if (!TryConfiguration(out var timeZone))
        {
            return ConfigurationMissing();
        }

        if (!settings.Value.Channels.Contains(channel, StringComparer.Ordinal))
        {
            return new ResponseDTO("Yêu cầu kênh bán hàng hợp lệ đã cấu hình.", 400);
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
            return new ResponseDTO("Không tìm thấy giá áp dụng cho món ăn, kênh bán và thời điểm này.", 404);
        }

        var priority = candidates.Max(price => Priority(price, channel!));
        var selected = candidates.Where(price => Priority(price, channel!) == priority).ToArray();
        if (selected.Length != 1)
        {
            return new ResponseDTO("Nhiều mức giá có cùng mức độ ưu tiên. Hãy xử lý lịch giá xung đột.", 409);
        }

        var result = selected[0];
        return new ResponseDTO("Lấy giá áp dụng thành công.", 200, true, new AppliedPriceDTO
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
            logger.LogWarning(exception, "Lưu giá thất bại: {Failure}", exception.Failure);
            return exception.Failure switch
            {
                PricePersistenceFailure.Conflict => Stale(),
                PricePersistenceFailure.RelatedDataChanged => new ResponseDTO("Dữ liệu liên quan đã thay đổi. Vui lòng tải lại và thử lại.", 409),
                PricePersistenceFailure.Busy => new ResponseDTO("Phát hiện thao tác đồng thời. Vui lòng thử lại.", 409),
                _ => new ResponseDTO("Kho dữ liệu giá hiện không khả dụng.", 503)
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
            return new ResponseDTO("Kênh bán hàng chưa được cấu hình định giá.", 400);
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
                return new ResponseDTO("EffectiveTo phải sau EffectiveFrom theo độ chính xác thời gian.", 400);
            }
        }

        if (!PriceRules.TryWindow(dailyStart, dailyEnd, out var windowStart, out var windowEnd))
        {
            return new ResponseDTO("Cung cấp cả hai khung giờ theo HH:mm hoặc HH:mm:ss, giờ kết thúc sau giờ bắt đầu. Không hỗ trợ khung giờ qua đêm.", 400);
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
        return PriceRules.TryConfiguration(settings.Value, out timeZone);
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
        return PriceRules.Priority(price, channel);
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
        logger.LogError("Vui lòng cấu hình Pricing:AllChannelsCode, Channels và BusinessTimeZone trước khi quản lý giá.");
        return new ResponseDTO("Chức năng định giá chưa khả dụng cho đến khi cấu hình các kênh và múi giờ kinh doanh.", 503);
    }

    private static ResponseDTO InvalidAmount()
    {
        return new ResponseDTO("Giá tiền phải không âm, tối đa 2 chữ số thập phân và theo định dạng decimal(18,2).", 400);
    }

    private static ResponseDTO InvalidTimestamp()
    {
        return new ResponseDTO("Vui lòng dùng mốc thời gian ISO 8601 có múi giờ UTC hợp lệ trong phạm vi datetime SQL.", 400);
    }

    private static ResponseDTO InvalidId()
    {
        return new ResponseDTO("Mã giá không hợp lệ.", 400);
    }

    private static ResponseDTO NotFound()
    {
        return new ResponseDTO("Không tìm thấy mức giá.", 404);
    }

    private static ResponseDTO ItemNotFound()
    {
        return new ResponseDTO("Không tìm thấy món ăn.", 404);
    }

    private static ResponseDTO Stale()
    {
        return new ResponseDTO("Mức giá đã thay đổi. Vui lòng tải lại và thử lại.", 409);
    }

    private static ResponseDTO Overlap()
    {
        return new ResponseDTO("Một mức giá khác bị trùng lặp phạm vi và thời gian hiệu lực.", 409);
    }
}
