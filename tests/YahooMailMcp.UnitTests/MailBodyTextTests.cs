using YahooMailMcp.Application;

namespace YahooMailMcp.UnitTests;

public sealed class MailBodyTextTests
{
    [Fact]
    public void NormalizesNewlinesAndReportsTruncation()
    {
        var result = MailBodyText.NormalizeAndCap("one\r\ntwo\rthree", 7);

        Assert.Equal("one\ntwo", result.Text);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void HtmlFallbackRemovesMarkupAndNonContent()
    {
        const string html = "<style>secret-style</style><p>Hello &amp; goodbye</p><script>secret-script</script>";

        var result = MailBodyText.FromHtml(html);

        Assert.Contains("Hello & goodbye", result, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-style", result, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-script", result, StringComparison.Ordinal);
    }
}