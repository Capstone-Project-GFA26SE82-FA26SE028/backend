using System.Globalization;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;
using Restaurant.Common.Settings;
using Restaurant.DAL.Interfaces;

namespace Restaurant.BLL.Implements;

internal static class ReservationRules
{
    private static readonly string[] TimestampFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"
    ];

    public static bool TryUtc(string? value, out DateTime utc)
    {
        utc = default;
        if (!DateTimeOffset.TryParseExact(value, TimestampFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return false;
        }

        utc = parsed.UtcDateTime;
        return utc >= new DateTime(1753, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            && utc <= new DateTime(9999, 12, 31, 23, 59, 59, 997, DateTimeKind.Utc);
    }

    public static void Configuration(ReservationSettings settings)
    {
        if (settings.DefaultDurationMinutes <= 0 || settings.MinimumAdvanceMinutes <= 0
            || settings.HoldBeforeMinutes <= 0 || settings.HoldAfterMinutes <= 0
            || settings.DurationMinutesByRestaurant is null
            || settings.DurationMinutesByRestaurant.Any(pair => pair.Key == Guid.Empty || pair.Value <= 0)
            || settings.ExpirationIntervalSeconds is < 1 or > 3600 || settings.ExpirationBatchSize is < 1 or > 1000)
        {
            throw new ManagementException(503, "Cấu hình thời lượng đặt bàn, giới hạn thời gian và hết hạn không hợp lệ.");
        }
    }

    public static ReservationWindowDto Window(
        Guid restaurantId, string timeZone, string? timestamp, DateTime now, ReservationSettings settings, IReservationRepository repository)
    {
        Configuration(settings);
        if (!TryUtc(timestamp, out var start))
        {
            throw new ManagementException(400, "StartAt phải là mốc thời gian ISO 8601 có múi giờ UTC hợp lệ trong phạm vi datetime SQL.");
        }

        start = repository.NormalizeTimestamp(start);
        try
        {
            var zone = GetTimeZone(timeZone);
            if (TimeZoneInfo.ConvertTimeFromUtc(start, zone).Date != TimeZoneInfo.ConvertTimeFromUtc(now, zone).Date)
            {
                throw new ManagementException(400, "Chỉ có thể đặt bàn trong ngày hiện tại theo múi giờ của nhà hàng.");
            }

            if (start < now.AddMinutes(settings.MinimumAdvanceMinutes))
            {
                throw new ManagementException(400, "StartAt nằm ngoài khoảng thời gian cho phép đặt trước.");
            }

            var duration = settings.DurationMinutesByRestaurant.TryGetValue(restaurantId, out var minutes)
                ? minutes
                : settings.DefaultDurationMinutes;
            var end = repository.NormalizeTimestamp(start.AddMinutes(duration));
            var holdStarts = repository.NormalizeTimestamp(start.AddMinutes(-settings.HoldBeforeMinutes));
            var holdUntil = repository.NormalizeTimestamp(start.AddMinutes(settings.HoldAfterMinutes));
            return new ReservationWindowDto
            {
                StartAt = start,
                EndAt = end,
                HoldStartsAt = holdStarts,
                HoldUntil = holdUntil,
                BlockedUntil = end > holdUntil ? end : holdUntil
            };
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException)
        {
            throw new ManagementException(400, "Khoảng thời gian đặt bàn tính toán vượt quá phạm vi mốc thời gian được hỗ trợ.");
        }
    }

    public static TimeZoneInfo GetTimeZone(string timeZone)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            throw new ManagementException(503, "Múi giờ kinh doanh của nhà hàng chưa được cấu hình chính xác.");
        }
    }

    public static DateTimeOffset Utc(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
