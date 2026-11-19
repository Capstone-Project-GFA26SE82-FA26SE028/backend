using System.Text.RegularExpressions;

namespace AI.BLL.Implements;

internal static class ChatPriceQuestion
{
    private const string Vietnamese = @"^(?:gia (?:cua )?{item}(?: la)?|{item}(?: co)?(?: gia)?) bao nhieu(?: tien)?(?: va (?:co |con )?dat duoc khong)?(?: nhe| a)?$";
    private const string English = @"^(?:how much is {item}|how much does {item} cost|what is (?:the )?price of {item})(?: and (?:can i order it|is it available))?(?: please)?$";
    private static readonly Regex UnrecognizedSymbol = Pattern(@"[^\p{L}\p{N}\s.,!?()\-]");

    public static bool TryGetLanguage(string question, string itemName, out string language)
    {
        var name = Words(ChatConversation.Fold(itemName));
        if (name.Length == 0 || UnrecognizedSymbol.IsMatch(question))
        {
            language = string.Empty;
            return false;
        }

        var subject = "(?:" + Regex.Escape(name) + "|mon do|mon nay|mon ay|that dish|this dish)";
        var text = Words(question);
        language = Pattern(Vietnamese.Replace("{item}", subject)).IsMatch(text) ? "vi"
            : Pattern(English.Replace("{item}", subject)).IsMatch(text) ? "en" : string.Empty;
        return language.Length > 0;
    }

    private static string Words(string value)
    {
        return string.Join(" ", Regex.Matches(value, @"[\p{L}\p{N}]+").Select(match => match.Value));
    }

    private static Regex Pattern(string pattern)
    {
        return new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }
}
