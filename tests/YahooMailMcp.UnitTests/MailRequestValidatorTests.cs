using YahooMailMcp.Application;
using YahooMailMcp.Domain;

namespace YahooMailMcp.UnitTests;

public sealed class MailRequestValidatorTests
{
    private static readonly MailLimits Limits = new();

    [Fact]
    public void EmptySearchIsRejected()
    {
        var request = new SearchMessagesRequest("INBOX");

        var exception = Assert.Throws<MailGatewayException>(() => MailRequestValidator.Validate(request, Limits));

        Assert.Equal(MailErrorCodes.InvalidRequest, exception.Code);
    }

    [Fact]
    public void PageLargerThanConfiguredMaximumIsRejected()
    {
        var request = new ListMessagesRequest("INBOX", Limits.MaxPageSize + 1);

        var exception = Assert.Throws<MailGatewayException>(() => MailRequestValidator.Validate(request, Limits));

        Assert.Equal(MailErrorCodes.ResultLimitExceeded, exception.Code);
    }

    [Fact]
    public void ReversedDateRangeIsRejected()
    {
        var now = DateTimeOffset.UtcNow;
        var request = new SearchMessagesRequest("INBOX", SinceUtc: now, BeforeUtc: now.AddDays(-1));

        var exception = Assert.Throws<MailGatewayException>(() => MailRequestValidator.Validate(request, Limits));

        Assert.Equal(MailErrorCodes.InvalidRequest, exception.Code);
    }

    [Fact]
    public void HardBodyLimitCannotExceedProductMaximum()
    {
        var limits = new MailLimits(HardMaxBodyCharacters: 200_001);

        Assert.Throws<ArgumentOutOfRangeException>(() => limits.Validate());
    }
}