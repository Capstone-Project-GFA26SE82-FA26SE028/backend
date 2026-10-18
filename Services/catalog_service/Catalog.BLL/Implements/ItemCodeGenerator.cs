using System.Globalization;
using System.Text;

namespace Catalog.BLL.Implements;

internal static class ItemCodeGenerator
{
    public static string Create(string name)
    {
        var builder = new StringBuilder();
        var separatorPending = false;
        foreach (var character in name.Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            var value = char.ToLowerInvariant(character);
            if (value == '\u0111')
            {
                value = 'd';
            }

            if (!char.IsAsciiLetterOrDigit(value))
            {
                separatorPending = builder.Length > 0;
                continue;
            }

            if (separatorPending)
            {
                builder.Append('-');
                separatorPending = false;
            }

            builder.Append(value);
        }

        return builder.ToString();
    }
}
