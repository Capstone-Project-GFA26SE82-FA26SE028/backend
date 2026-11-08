using System.Globalization;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;

namespace Restaurant.BLL.Implements;

internal static class ManagementValidation
{
    public static string Required(string? value, int max, string field)
    {
        var result = value?.Trim();
        if (string.IsNullOrEmpty(result) || result.Length > max)
        {
            throw new ManagementException(400, $"{field} là bắt buộc và không được vượt quá {max} ký tự.");
        }

        return result;
    }

    public static string? Optional(string? value, int max, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Required(value, max, field);
    }

    public static void Query(ListQuery query)
    {
        if (query.Page < 1 || query.PageSize < 1 || query.PageSize > 100
            || query.Page > int.MaxValue / query.PageSize || query.Search?.Length > 255)
        {
            throw new ManagementException(400, "Phân trang hoặc tìm kiếm không hợp lệ (page >= 1, pageSize 1-100).");
        }
    }

    public static void Version(byte[] current, string supplied)
    {
        byte[] version;
        try
        {
            version = Convert.FromBase64String(supplied ?? "");
        }
        catch (FormatException)
        {
            throw new ManagementException(400, "Version phải là chuỗi Base64 hợp lệ.");
        }

        if (version.Length == 0)
        {
            throw new ManagementException(400, "Vui lòng cung cấp Version nhận được từ lần đọc mới nhất.");
        }

        if (!current.SequenceEqual(version))
        {
            throw new ManagementException(409, "Dữ liệu bàn đã thay đổi. Vui lòng tải lại dữ liệu.");
        }
    }

    public static TimeOnly Time(string value)
    {
        if (!TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
        {
            throw new ManagementException(400, "Giờ hoạt động phải theo định dạng HH:mm.");
        }

        return result;
    }
}
