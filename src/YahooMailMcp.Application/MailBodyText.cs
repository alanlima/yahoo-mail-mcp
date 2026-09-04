using System.Net;
using System.Text.RegularExpressions;

namespace YahooMailMcp.Application;

public static partial class MailBodyText
{
    public static (string? Text, bool Truncated) NormalizeAndCap(string? text, int maximumCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCharacters, 1);

        if (text is null)
        {
            return (null, false);
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return normalized.Length <= maximumCharacters
            ? (normalized, false)
            : (normalized[..maximumCharacters], true);
    }

    public static string FromHtml(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var withoutNonContent = NonContentHtmlRegex().Replace(html, " ");
        var withLineBreaks = HtmlBreakRegex().Replace(withoutNonContent, "\n");
        var withoutTags = HtmlTagRegex().Replace(withLineBreaks, " ");
        return WebUtility.HtmlDecode(withoutTags);
    }

    [GeneratedRegex("<(script|style)\\b[^>]*>.*?</\\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1_000)]
    private static partial Regex NonContentHtmlRegex();

    [GeneratedRegex("<(br|/p|/div|/li|/tr|/h[1-6])\\b[^>]*>", RegexOptions.IgnoreCase, 1_000)]
    private static partial Regex HtmlBreakRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline, 1_000)]
    private static partial Regex HtmlTagRegex();
}