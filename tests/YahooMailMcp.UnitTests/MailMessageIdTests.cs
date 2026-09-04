using YahooMailMcp.Domain;

namespace YahooMailMcp.UnitTests;

public sealed class MailMessageIdTests
{
    [Fact]
    public void EqualityIncludesFolderUidAndUidValidity()
    {
        var first = new MailMessageId("Inbox", 42, 7);
        var same = new MailMessageId("Inbox", 42, 7);
        var differentEpoch = new MailMessageId("Inbox", 42, 8);

        Assert.Equal(first, same);
        Assert.NotEqual(first, differentEpoch);
    }
}