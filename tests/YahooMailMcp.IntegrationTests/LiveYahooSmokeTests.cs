using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using YahooMailMcp.Application;
using YahooMailMcp.Infrastructure.MailKit;

namespace YahooMailMcp.IntegrationTests;

public sealed class LiveYahooSmokeTests
{
    [LiveYahooFact]
    [Trait("Category", "LiveYahoo")]
    public async Task ListsLatestInboxMessagesWithoutDisplayingPersonalData()
    {
        var options = Options.Create(new YahooOptions
        {
            Email = Environment.GetEnvironmentVariable("YAHOO__EMAIL"),
            AppPassword = Environment.GetEnvironmentVariable("YAHOO__APPPASSWORD")
        });
        var authenticator = new AppPasswordImapAuthenticator(options);
        await using var session = new YahooImapSession(
            authenticator,
            options,
            NullLogger<YahooImapSession>.Instance);
        var gateway = new YahooMailGateway(
            session,
            new CursorCodec(Environment.GetEnvironmentVariable("CURSOR__SIGNINGKEY")!),
            new MailLimits(),
            TimeProvider.System,
            new DestinationFolderPolicy(options.Value.TrashFolderDenyList));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var status = await gateway.GetStatusAsync(timeout.Token);
        var messages = await gateway.ListMessagesAsync(new ListMessagesRequest("INBOX", 10), timeout.Token);

        Assert.True(status.Authenticated);
        Assert.InRange(messages.Items.Count, 0, 10);
    }
}

public sealed class LiveYahooFactAttribute : FactAttribute
{
    public LiveYahooFactAttribute()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("RUN_LIVE_YAHOO_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Live Yahoo access is explicitly opt-in and never runs in CI.";
        }
    }
}