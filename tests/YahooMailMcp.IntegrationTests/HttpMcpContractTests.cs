using System.Collections.Concurrent;
using System.Net;
using System.Text;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using ModelContextProtocol.Client;

namespace YahooMailMcp.IntegrationTests;

public sealed class HttpMcpContractTests : IClassFixture<HttpMcpContractTests.McpApplicationFactory>
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private readonly McpApplicationFactory factory;

    public HttpMcpContractTests(McpApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task MissingCredentialIsRejected()
    {
        using var client = factory.CreateClient();
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/mcp", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InvalidApiKeyIsRejected()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("ApiKey", "not-the-configured-key");
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/mcp", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedOfficialClientListsExactToolSurface()
    {
        using var httpClient = factory.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(httpClient.BaseAddress!, "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                EnableStandaloneGetStream = false,
                AdditionalHeaders = new Dictionary<string, string>
                {
                    ["Authorization"] = $"ApiKey {ApiKey}"
                }
            },
            httpClient,
            NullLoggerFactory.Instance,
            ownsHttpClient: false);
        await using var mcpClient = await McpClient.CreateAsync(transport);

        var tools = await mcpClient.ListToolsAsync();
        var names = tools.Select(tool => tool.Name).Order().ToArray();

        Assert.Equal(
            [
                "yahoo_mail_get_message",
                "yahoo_mail_list_folders",
                "yahoo_mail_list_messages",
                "yahoo_mail_mark_read",
                "yahoo_mail_mark_unread",
                "yahoo_mail_move_message",
                "yahoo_mail_search_messages",
                "yahoo_mail_set_flagged",
                "yahoo_mail_status"
            ],
            names);

        var result = await mcpClient.CallToolAsync(
            "yahoo_mail_list_messages",
            new Dictionary<string, object?>
            {
                ["folder"] = "sensitive-folder-value-must-not-be-logged",
                ["limit"] = 0
            });

        Assert.NotNull(result.StructuredContent);
        Assert.Contains("\"success\":false", result.StructuredContent.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invalid_request", result.StructuredContent.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(factory.LogMessages, message =>
            message.Contains("tool.name=yahoo_mail_list_messages", StringComparison.Ordinal));
        Assert.DoesNotContain(factory.LogMessages, message =>
            message.Contains("sensitive-folder-value-must-not-be-logged", StringComparison.Ordinal));
    }

    public sealed class McpApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly CollectingLoggerProvider loggerProvider = new();

        public IReadOnlyCollection<string> LogMessages => loggerProvider.Messages;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureLogging(logging => logging.AddProvider(loggerProvider));
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Yahoo:Email"] = "configured-test-address",
                    ["Yahoo:AppPassword"] = "configured-test-app-password",
                    ["Cursor:SigningKey"] = "0123456789abcdef0123456789abcdef",
                    ["Mcp:BearerToken"] = ApiKey
                });
            });
        }
    }

    private sealed class CollectingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> messages = new();

        public IReadOnlyCollection<string> Messages => messages.ToArray();

        public ILogger CreateLogger(string categoryName) => new CollectingLogger(messages);

        public void Dispose()
        {
        }

        private sealed class CollectingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception));
        }
    }
}