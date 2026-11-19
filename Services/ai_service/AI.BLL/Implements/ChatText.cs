using System.Globalization;
using AI.Common.DTOs;

namespace AI.BLL.Implements;

internal static class ChatText
{
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");

    public static string Pick(string language, string english, string vietnamese)
    {
        return language == "vi" ? vietnamese : english;
    }

    public static string MissingFoodFacts(string language)
    {
        return Pick(language, AssistantRules.MissingFoodInformation,
            "Mình chưa có thông tin xác nhận về thành phần, dị ứng, chế độ ăn hoặc độ cay. Bạn vui lòng hỏi nhân viên trước khi gọi món.");
    }

    public static string ItemLabel(string name)
    {
        var line = name.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return line[..Math.Min(line.Length, 90)];
    }

    public static string Item(MenuItemDTO item, string language, decimal? budget, bool strict)
    {
        var amount = item.Price is decimal price ? Money(price, language)
            : Pick(language, "price not available", "chưa có giá");
        var availability = item.CanOrder
            ? Pick(language, "Available to order.", "Hiện có thể đặt.")
            : Pick(language, "Not currently available to order.", "Hiện không thể đặt.");
        var line = $"{ItemLabel(item.ItemName)}: {amount}. {availability}";
        if (budget is decimal limit && item.Price is decimal value)
        {
            var amountLimit = Money(limit, language);
            var comparison = value < limit ? Pick(language, $"Below {amountLimit}.", $"Dưới {amountLimit}.")
                : value == limit ? Pick(language, $"Exactly {amountLimit}.", $"Đúng {amountLimit}.")
                : Pick(language, $"Above {amountLimit}.", $"Cao hơn {amountLimit}.");
            line += " " + comparison;
            if (strict && value == limit)
            {
                line += Pick(language, " This does not meet a strict lower-than limit.", " Món này chưa đáp ứng yêu cầu thấp hơn mức đó.");
            }
        }

        return line;
    }

    public static string Money(decimal amount, string language)
    {
        return language == "vi" ? amount.ToString("#,0.##", Vietnamese) + " ₫"
            : amount.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
