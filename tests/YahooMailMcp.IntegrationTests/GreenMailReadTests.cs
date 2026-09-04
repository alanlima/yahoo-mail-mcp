using DotNet.Testcontainers.Builders;

using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using MimeKit;

using YahooMailMcp.Application;
using YahooMailMcp.Domain;
using YahooMailMcp.Infrastructure.MailKit;

namespace YahooMailMcp.IntegrationTests;

public sealed class GreenMailReadTests
{
    private const string Image = "greenmail/standalone@sha256:3df66b7edd01c8a301343ca5e3601d8674760d4708655573560c24745e624fb2";
    private const string Email = "test-user@example.test";
    private const string Password = "deterministic-test-password";

    [DockerFact]
    public async Task GatewayReadsAndSafelyOrganizesSeededInboxMessage()
    {
        await using var container = new ContainerBuilder(Image)
            .WithPortBinding(3025, assignRandomHostPort: true)
            .WithPortBinding(3143, assignRandomHostPort: true)
            .WithEnvironment(
                "GREENMAIL_OPTS",
                $"-Dgreenmail.setup.test.smtp -Dgreenmail.setup.test.imap -Dgreenmail.hostname=0.0.0.0 -Dgreenmail.users=test-user:{Password}@example.test -Dgreenmail.users.login=email")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(3143))
            .Build();

        await container.StartAsync();
        await SeedMessageAsync(container.Hostname, container.GetMappedPublicPort(3025));
        await CreateFoldersAsync(container.Hostname, container.GetMappedPublicPort(3143));

        var yahooOptions = Options.Create(new YahooOptions
        {
            Host = container.Hostname,
            Port = container.GetMappedPublicPort(3143),
            UseSsl = false,
            Email = Email,
            AppPassword = Password,
            AllowNonYahooEndpoint = true
        });
        var authenticator = new AppPasswordImapAuthenticator(yahooOptions);
        await using var session = new YahooImapSession(
            authenticator,
            yahooOptions,
            NullLogger<YahooImapSession>.Instance);
        var gateway = new YahooMailGateway(
            session,
            new CursorCodec("0123456789abcdef0123456789abcdef"),
            new MailLimits(),
            TimeProvider.System,
            new DestinationFolderPolicy(["Trash", "Bin", "Deleted Items", "Deleted Messages"]));
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var result = await gateway.ListMessagesAsync(
            new ListMessagesRequest("INBOX", 10),
            cancellationTokenSource.Token);

        var message = Assert.Single(result.Items);
        Assert.Equal("Deterministic integration message", message.Subject);
        Assert.Equal(Email, Assert.Single(message.To).Address);
        Assert.False(message.HasAttachments);
        Assert.Null(result.NextCursor);

        var readResult = await gateway.SetReadStateAsync(
            new SetReadStateRequest(message.Id, IsRead: true),
            cancellationTokenSource.Token);
        var flaggedResult = await gateway.SetFlaggedStateAsync(
            new SetFlaggedStateRequest(message.Id, IsFlagged: true),
            cancellationTokenSource.Token);
        var refreshed = await gateway.ListMessagesAsync(
            new ListMessagesRequest("INBOX", 10),
            cancellationTokenSource.Token);

        Assert.True(readResult.IsRead);
        Assert.True(flaggedResult.IsFlagged);
        Assert.True(Assert.Single(refreshed.Items).Flags.HasFlag(MailMessageState.Seen));
        Assert.True(Assert.Single(refreshed.Items).Flags.HasFlag(MailMessageState.Flagged));

        var deniedMove = await Assert.ThrowsAsync<MailGatewayException>(() => gateway.MoveMessageAsync(
            new MoveMessageRequest(message.Id, "Bin"),
            cancellationTokenSource.Token));
        Assert.Equal(MailErrorCodes.TrashDestinationForbidden, deniedMove.Code);
        Assert.Single((await gateway.ListMessagesAsync(
            new ListMessagesRequest("INBOX", 10),
            cancellationTokenSource.Token)).Items);

        var status = await gateway.GetStatusAsync(cancellationTokenSource.Token);
        if (status.Features.NativeMoveSupported)
        {
            var moved = await gateway.MoveMessageAsync(
                new MoveMessageRequest(message.Id, "SafeArchive"),
                cancellationTokenSource.Token);

            Assert.True(moved.Completed);
            Assert.Empty((await gateway.ListMessagesAsync(
                new ListMessagesRequest("INBOX", 10),
                cancellationTokenSource.Token)).Items);
            Assert.Single((await gateway.ListMessagesAsync(
                new ListMessagesRequest("SafeArchive", 10),
                cancellationTokenSource.Token)).Items);
        }
        else
        {
            var unsupportedMove = await Assert.ThrowsAsync<MailGatewayException>(() => gateway.MoveMessageAsync(
                new MoveMessageRequest(message.Id, "SafeArchive"),
                cancellationTokenSource.Token));

            Assert.Equal(MailErrorCodes.OperationNotSupported, unsupportedMove.Code);
            Assert.Single((await gateway.ListMessagesAsync(
                new ListMessagesRequest("INBOX", 10),
                cancellationTokenSource.Token)).Items);
        }
    }

    private static async Task CreateFoldersAsync(string host, ushort port)
    {
        using var imapClient = new ImapClient();
        await imapClient.ConnectAsync(host, port, SecureSocketOptions.None).ConfigureAwait(false);
        await imapClient.AuthenticateAsync(Email, Password).ConfigureAwait(false);
        var root = imapClient.GetFolder(imapClient.PersonalNamespaces[0]);
        await root.CreateAsync("SafeArchive", isMessageFolder: true).ConfigureAwait(false);
        await root.CreateAsync("Bin", isMessageFolder: true).ConfigureAwait(false);
        await imapClient.DisconnectAsync(quit: true).ConfigureAwait(false);
    }

    private static async Task SeedMessageAsync(string host, ushort port)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse("sender@example.test"));
        message.To.Add(MailboxAddress.Parse(Email));
        message.Subject = "Deterministic integration message";
        message.Body = new TextPart("plain") { Text = "Integration body that must not appear in logs." };

        using var smtpClient = new SmtpClient();
        await smtpClient.ConnectAsync(host, port, SecureSocketOptions.None).ConfigureAwait(false);
        await smtpClient.SendAsync(message).ConfigureAwait(false);
        await smtpClient.DisconnectAsync(quit: true).ConfigureAwait(false);
    }
}

public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("RUN_IMAP_INTEGRATION_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Set RUN_IMAP_INTEGRATION_TESTS=true with Docker available to run this test.";
        }
    }
}