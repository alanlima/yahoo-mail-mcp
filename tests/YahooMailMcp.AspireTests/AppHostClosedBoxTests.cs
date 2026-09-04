using Aspire.Hosting;

using MailKit.Net.Smtp;
using MailKit.Security;

using Microsoft.Extensions.Logging.Abstractions;

using MimeKit;

using ModelContextProtocol.Client;

namespace YahooMailMcp.AspireTests;

public sealed class AppHostClosedBoxTests
{
    [ClosedBoxFact]
    public async Task LocalProfileServesSeededMailThroughAuthenticatedMcp()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.YahooMailMcp_AppHost>(
            ["--YahooMail:Profile=local-imap"],
            timeout.Token);
        await using var app = await builder.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);

        var smtpEndpoint = app.GetEndpoint("imap-test", "smtp");
        await SeedMessageWithRetryAsync(smtpEndpoint, timeout.Token);

        var httpEndpoint = app.GetEndpoint("yahoo-mail-mcp-http", "http");
        using var httpClient = new HttpClient { BaseAddress = httpEndpoint };
        await WaitForHealthAsync(httpClient, timeout.Token);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(httpEndpoint, "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                EnableStandaloneGetStream = false,
                AdditionalHeaders = new Dictionary<string, string>
                {
                    ["Authorization"] = "ApiKey local-test-mcp-bearer-token-32-bytes"
                }
            },
            httpClient,
            NullLoggerFactory.Instance,
            ownsHttpClient: false);
        await using var mcpClient = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);

        var tools = await mcpClient.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(9, tools.Count);
        var result = await mcpClient.CallToolAsync(
            "yahoo_mail_list_messages",
            new Dictionary<string, object?> { ["folder"] = "INBOX", ["limit"] = 10 },
            cancellationToken: timeout.Token);

        Assert.NotNull(result.StructuredContent);
        Assert.Contains("\"success\":true", result.StructuredContent.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"items\":[{", result.StructuredContent.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task SeedMessageWithRetryAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                var message = new MimeMessage();
                message.From.Add(MailboxAddress.Parse("sender@example.test"));
                message.To.Add(MailboxAddress.Parse("test-user@example.test"));
                message.Subject = "Aspire closed-box sentinel subject";
                message.Body = new TextPart("plain") { Text = "Aspire closed-box sentinel body." };
                using var smtp = new SmtpClient
                {
                    Timeout = 5_000
                };
                await smtp.ConnectAsync(
                    endpoint.Host,
                    endpoint.Port,
                    SecureSocketOptions.None,
                    cancellationToken);
                await smtp.SendAsync(message, cancellationToken: cancellationToken);
                await smtp.DisconnectAsync(quit: true, cancellationToken);
                return;
            }
            catch (Exception exception) when (
                exception is HttpRequestException or IOException or SmtpCommandException or SmtpProtocolException)
            {
                lastError = exception;
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        throw new InvalidOperationException("The local SMTP test dependency did not become ready.", lastError);
    }

    private static async Task WaitForHealthAsync(HttpClient client, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                using var response = await client.GetAsync("/health/ready", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException exception)
            {
                lastError = exception;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        throw new InvalidOperationException("The Aspire HTTP resource did not become healthy.", lastError);
    }
}

public sealed class ClosedBoxFactAttribute : FactAttribute
{
    public ClosedBoxFactAttribute()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("RUN_ASPIRE_CLOSED_BOX_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Set RUN_ASPIRE_CLOSED_BOX_TESTS=true with Docker available to run the closed-box Aspire test.";
        }
    }
}
