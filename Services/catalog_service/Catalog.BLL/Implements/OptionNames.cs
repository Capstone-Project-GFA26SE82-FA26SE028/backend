using System.Text;

namespace Catalog.BLL.Implements;

internal static class OptionNames
{
    public static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);
    }

    public static bool IsValid(string value)
    {
        return value.Length is >= 1 and <= 255 && !value.Any(char.IsControl);
    }
}
