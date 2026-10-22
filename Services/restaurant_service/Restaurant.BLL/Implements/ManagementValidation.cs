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
            throw new ManagementException(400, $"{field} is required and must not exceed {max} characters.");
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
            throw new ManagementException(400, "Invalid pagination or search (page >= 1, pageSize 1-100).");
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
            throw new ManagementException(400, "Version must be a Base64 string.");
        }

        if (version.Length == 0)
        {
            throw new ManagementException(400, "Provide the Version returned by the latest read.");
        }

        if (!current.SequenceEqual(version))
        {
            throw new ManagementException(409, "The table has changed. Reload the data.");
        }
    }

    public static TimeOnly Time(string value)
    {
        if (!TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
        {
            throw new ManagementException(400, "Operating hours must use the HH:mm format.");
        }

        return result;
    }
}
