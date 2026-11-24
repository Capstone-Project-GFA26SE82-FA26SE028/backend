using System.Globalization;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;

namespace Billing.BLL.Implements;

public sealed class RestaurantReportService(IRestaurantReportContextClient restaurants) : IRestaurantReportService
{
    public async Task<ReportPeriodDTO> GetPeriod(Guid restaurantId, ReportPeriodQueryDTO query, CancellationToken ct)
    {
        if (restaurantId == Guid.Empty)
        {
            throw new BillingException(400, "Yêu cầu mã nhà hàng hợp lệ.");
        }

        // Authorization is performed by Restaurant before returning the restaurant's time zone.
        var context = await restaurants.GetAsync(restaurantId, ct);
        if (context.RestaurantId != restaurantId)
        {
            throw new BillingException(503, "Không thể xác minh ngữ cảnh báo cáo của nhà hàng.");
        }

        var from = Parse(query.From);
        var to = Parse(query.To);
        if (from >= to)
        {
            throw new BillingException(400, "Thời điểm bắt đầu phải nhỏ hơn thời điểm kết thúc.");
        }

        var grouping = query.GroupBy?.ToLowerInvariant();
        if (grouping is not ("hour" or "day" or "week" or "month"))
        {
            throw new BillingException(400, "Chọn cách nhóm hour, day, week hoặc month.");
        }

        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(context.TimeZone);
        }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            throw new BillingException(503, "Múi giờ nhà hàng chưa được cấu hình hợp lệ.");
        }

        return new ReportPeriodDTO(
            restaurantId,
            zone.Id,
            from.ToUniversalTime(),
            to.ToUniversalTime(),
            TimeZoneInfo.ConvertTime(from, zone),
            TimeZoneInfo.ConvertTime(to, zone),
            grouping,
            "Monday");
    }

    private static DateTimeOffset Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
        {
            throw new BillingException(400, "Nhập thời gian ISO 8601 có Z hoặc offset, ví dụ 2026-11-25T00:00:00+07:00.");
        }

        if (value.EndsWith('z'))
        {
            value = value[..^1] + "Z";
        }

        var hasOffset = value.EndsWith('Z') || (value.Length >= 6
            && (value[^6] is '+' or '-') && value[^3] == ':');
        string[] formats = ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"];
        if (!hasOffset || !DateTimeOffset.TryParseExact(value, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var result))
        {
            throw new BillingException(400, "Thời gian phải theo ISO 8601, có Z hoặc offset rõ ràng.");
        }

        return result;
    }
}
