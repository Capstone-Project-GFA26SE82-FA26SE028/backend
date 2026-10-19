using System.Globalization;
using Catalog.DAL.Models;

namespace Catalog.BLL.Implements;

internal static class PriceRules
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
            DateTimeStyles.AssumeUniversal, out var timestamp))
        {
            return false;
        }

        utc = timestamp.UtcDateTime;
        return utc >= new DateTime(1753, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            && utc <= new DateTime(9999, 12, 31, 23, 59, 59, 997, DateTimeKind.Utc);
    }

    public static bool TryAmount(string? text, out decimal amount)
    {
        return decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out amount)
            && amount >= 0 && amount <= 9999999999999999.99m
            && ((decimal.GetBits(amount)[3] >> 16) & 0xFF) <= 2;
    }

    public static bool TryWindow(string? start, string? end, out TimeOnly? dailyStart, out TimeOnly? dailyEnd)
    {
        dailyStart = null;
        dailyEnd = null;
        if (start is null && end is null)
        {
            return true;
        }

        if (!TimeOnly.TryParseExact(start, ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var from)
            || !TimeOnly.TryParseExact(end, ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var to)
            || to <= from)
        {
            return false;
        }

        dailyStart = from;
        dailyEnd = to;
        return true;
    }

    public static bool AppliesAt(Price price, DateTime utc, TimeZoneInfo timeZone)
    {
        if (utc < price.EffectiveFrom || (price.EffectiveTo.HasValue && utc >= price.EffectiveTo.Value))
        {
            return false;
        }

        if (!price.DailyStart.HasValue && !price.DailyEnd.HasValue)
        {
            return true;
        }

        if (!price.DailyStart.HasValue || !price.DailyEnd.HasValue || price.DailyEnd <= price.DailyStart)
        {
            return false;
        }

        var localTime = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc), timeZone));
        return localTime >= price.DailyStart.Value && localTime < price.DailyEnd.Value;
    }

    public static bool Overlaps(Price left, Price right, TimeZoneInfo timeZone)
    {
        if (!string.Equals(left.Channel, right.Channel, StringComparison.Ordinal)
            || left.DailyStart.HasValue != right.DailyStart.HasValue)
        {
            return false;
        }

        var from = left.EffectiveFrom > right.EffectiveFrom ? left.EffectiveFrom : right.EffectiveFrom;
        var to = (left.EffectiveTo ?? DateTime.MaxValue) < (right.EffectiveTo ?? DateTime.MaxValue)
            ? left.EffectiveTo ?? DateTime.MaxValue
            : right.EffectiveTo ?? DateTime.MaxValue;
        if (from >= to)
        {
            return false;
        }

        if (!left.DailyStart.HasValue)
        {
            return true;
        }

        if (!left.DailyEnd.HasValue || !right.DailyEnd.HasValue)
        {
            return true;
        }

        var start = left.DailyStart.Value > right.DailyStart!.Value ? left.DailyStart.Value : right.DailyStart.Value;
        var end = left.DailyEnd.Value < right.DailyEnd.Value ? left.DailyEnd.Value : right.DailyEnd.Value;
        if (start >= end)
        {
            return false;
        }

        var localFrom = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(from, DateTimeKind.Utc), timeZone);
        var localTime = TimeOnly.FromDateTime(localFrom);
        if (localTime >= start && localTime < end)
        {
            return true;
        }

        var date = localFrom.Date;
        if (localTime >= end)
        {
            if (date == DateTime.MaxValue.Date)
            {
                return false;
            }

            date = date.AddDays(1);
        }

        var nextStart = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(date.Add(start.ToTimeSpan()), DateTimeKind.Unspecified), timeZone);
        return nextStart >= from && nextStart < to;
    }
}
