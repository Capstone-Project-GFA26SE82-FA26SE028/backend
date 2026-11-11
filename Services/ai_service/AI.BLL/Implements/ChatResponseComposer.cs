using System.Globalization;
using AI.Common.DTOs;
using AI.Common.Exceptions;

namespace AI.BLL.Implements;

internal static class ChatResponseComposer
{
    public static void ValidateLanguage(string language)
    {
        if (language == "unsupported")
        {
            throw new AIException(400, "This conversation language is not supported. No assistant answer was generated.");
        }

        if (language is not ("vi" or "en"))
        {
            throw new AIException(502, "The assistant returned an invalid conversation language.");
        }
    }

    public static string Notice(string language, string intent, bool limited)
    {
        var text = (language, intent) switch
        {
            ("vi", "food_information") => "Chưa có thông tin được xác nhận về chế độ ăn, thành phần, dị ứng và độ cay. Vui lòng hỏi nhân viên để được giải thích rõ hơn trước khi gọi món.",
            ("en", "food_information") => AssistantRules.MissingFoodInformation,
            ("vi", "food_concern") => "Nếu nghi ngờ món ăn có vấn đề, vui lòng ngừng ăn và liên hệ nhân viên để được hỗ trợ. Tôi không thể xác nhận nguyên nhân hoặc tự liên hệ nhân viên thay bạn.",
            ("en", "food_concern") => "If you suspect a problem with your food, stop eating it and contact a staff member for assistance. I cannot confirm the cause or contact staff on your behalf.",
            ("vi", "restaurant") => "Trợ lý chưa có thông tin được xác nhận về liên hệ, giờ mở cửa và chính sách phục vụ của nhà hàng. Vui lòng hỏi nhân viên để được giải thích rõ hơn.",
            ("en", "restaurant") => AssistantRules.MissingRestaurantInformation,
            ("vi", "out_of_scope") => "Tôi có thể hỗ trợ về thực đơn công khai, giá và khả năng đặt món. Với các vấn đề khác của nhà hàng, vui lòng liên hệ nhân viên.",
            ("en", "out_of_scope") => "I can help with the public menu, prices and dish availability. For other restaurant matters, please contact a staff member.",
            ("vi", "no_match") => "Chưa tìm thấy món phù hợp trong phần thực đơn đã kiểm tra. Bạn có thể nêu tên món hoặc hỏi nhân viên.",
            ("en", "no_match") => "I could not find a matching dish in the current selection. Please specify a dish name or ask a staff member.",
            _ => throw new AIException(502, "The assistant selected an unsupported response.")
        };

        return WithSearchLimit(language, text, limited);
    }

    public static string Menu(string language, string topic, IReadOnlyList<MenuItemDTO> items, bool limited)
    {
        if (items.Count == 0)
        {
            return Notice(language, "no_match", limited);
        }

        var sentences = items.Select(item => topic switch
        {
            "price" => Price(language, item),
            "availability" => Availability(language, item),
            "options" => Options(language, item),
            "overview" => Price(language, item) + " " + Availability(language, item),
            _ => throw new AIException(502, "The assistant selected an unsupported menu topic.")
        });

        return WithSearchLimit(language, string.Join(" ", sentences), limited);
    }

    private static string Price(string language, MenuItemDTO item)
    {
        if (!item.Price.HasValue)
        {
            return language == "vi"
                ? $"{item.ItemName} hiện chưa có giá áp dụng hợp lệ."
                : $"{item.ItemName} currently has no valid applicable price.";
        }

        var amount = item.Price.Value.ToString("0.##", CultureInfo.InvariantCulture);
        return language == "vi"
            ? $"{item.ItemName} có giá cơ bản {amount} VND."
            : $"{item.ItemName} has a base price of {amount} VND.";
    }

    private static string Availability(string language, MenuItemDTO item)
    {
        var text = (language, item.CanOrder) switch
        {
            ("vi", true) => $"{item.ItemName} hiện có thể đặt theo thực đơn.",
            ("en", true) => $"{item.ItemName} is currently orderable according to the menu.",
            ("vi", false) => $"{item.ItemName} hiện không thể đặt theo thực đơn.",
            _ => $"{item.ItemName} is not currently orderable according to the menu."
        };

        if (item.RequiresOptionSelection)
        {
            text += language == "vi"
                ? " Cần chọn tùy chọn bắt buộc trước khi thêm món."
                : " Required options must be selected before adding this dish.";
        }

        return text;
    }

    private static string Options(string language, MenuItemDTO item)
    {
        return language == "vi"
            ? $"Trợ lý chưa có danh sách chi tiết và phụ thu tùy chọn của {item.ItemName}. Vui lòng kiểm tra phần tùy chọn trên thực đơn hoặc hỏi nhân viên."
            : $"The assistant does not have the detailed options and surcharges for {item.ItemName}. Please check the menu options or ask a staff member.";
    }

    private static string WithSearchLimit(string language, string text, bool limited)
    {
        if (!limited)
        {
            return text;
        }

        return text + (language == "vi"
            ? " Chỉ một phần thực đơn đã được kiểm tra."
            : " Only a limited selection of the menu was checked.");
    }
}
