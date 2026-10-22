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
            || settings.MaximumAdvanceDays <= 0 || settings.HoldBeforeMinutes <= 0 || settings.HoldAfterMinutes <= 0
            || settings.DurationMinutesByRestaurant is null
            || settings.DurationMinutesByRestaurant.Any(pair => pair.Key == Guid.Empty || pair.Value <= 0)
            || settings.ExpirationIntervalSeconds is < 1 or > 3600 || settings.ExpirationBatchSize is < 1 or > 1000)
        {
            throw new ManagementException(503, "Configure valid reservation durations, time limits, and expiration settings.");
        }
    }

    public static ReservationWindowDto Window(
        Guid restaurantId, string? timestamp, DateTime now, ReservationSettings settings, IReservationRepository repository)
    {
        Configuration(settings);
        if (!TryUtc(timestamp, out var start))
        {
            throw new ManagementException(400, "StartAt must be an ISO 8601 timestamp with an explicit UTC offset within the SQL datetime range.");
        }

        start = repository.NormalizeTimestamp(start);
        try
        {
            if (start < now.AddMinutes(settings.MinimumAdvanceMinutes) || start > now.AddDays(settings.MaximumAdvanceDays))
            {
                throw new ManagementException(400, "StartAt is outside the configured advance booking interval.");
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
            throw new ManagementException(400, "The calculated reservation interval is outside the supported timestamp range.");
        }
    }

    public static DateTimeOffset Utc(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
