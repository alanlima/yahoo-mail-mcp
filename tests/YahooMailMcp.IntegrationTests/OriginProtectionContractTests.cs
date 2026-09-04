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

public sealed class OriginProtectionContractTests
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string OriginSecret = "origin-verification-test-value-32-bytes";
    private const string OriginHeader = "X-Origin-Verify";

    [Fact]
    public async Task CorrectOriginHeaderAndValidAuthenticationReachMcp()
    {
        using var factory = new OriginProtectedApplicationFactory(enabled: true);
        using var httpClient = factory.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(httpClient.BaseAddress!, "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                EnableStandaloneGetStream = false,
                AdditionalHeaders = new Dictionary<string, string>
                {
                    ["Authorization"] = $"ApiKey {ApiKey}",
                    [OriginHeader] = OriginSecret
                }
            },
            httpClient,
            NullLoggerFactory.Instance,
            ownsHttpClient: false);

        await using var client = await McpClient.CreateAsync(transport);
        var tools = await client.ListToolsAsync();

        Assert.Equal(9, tools.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("incorrect-origin-verification-value-32-bytes")]
    public async Task MissingOrIncorrectOriginHeaderIsRejected(string? originHeaderValue)
    {
        using var factory = new OriginProtectedApplicationFactory(enabled: true);
        using var client = factory.CreateClient();
        using var request = CreateMcpRequest(originHeaderValue);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpointsBypassOriginProtection()
    {
        using var factory = new OriginProtectedApplicationFactory(enabled: true);
        using var client = factory.CreateClient();

        using var live = await client.GetAsync("/health/live");
        using var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public async Task DisabledOriginProtectionPreservesLocalBehavior()
    {
        using var factory = new OriginProtectedApplicationFactory(enabled: false);
        using var client = factory.CreateClient();
        using var request = CreateMcpRequest(originHeaderValue: null);

        using var response = await client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OriginHeaderValueIsNeverLogged()
    {
        using var factory = new OriginProtectedApplicationFactory(enabled: true);
        using var client = factory.CreateClient();
        using var request = CreateMcpRequest("incorrect-origin-verification-value-32-bytes");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(factory.LogMessages, message =>
            message.Contains("incorrect-origin-verification-value-32-bytes", StringComparison.Ordinal));
        Assert.DoesNotContain(factory.LogMessages, message =>
            message.Contains(OriginSecret, StringComparison.Ordinal));
    }

    private static HttpRequestMessage CreateMcpRequest(string? originHeaderValue)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new("ApiKey", ApiKey);
        if (originHeaderValue is not null)
        {
            request.Headers.Add(OriginHeader, originHeaderValue);
        }

        return request;
    }

    private sealed class OriginProtectedApplicationFactory(bool enabled) : WebApplicationFactory<Program>
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
                    ["Mcp:BearerToken"] = ApiKey,
                    ["OriginProtection:Enabled"] = enabled.ToString(),
                    ["OriginProtection:HeaderName"] = OriginHeader,
                    ["OriginProtection:HeaderValue"] = enabled ? OriginSecret : null
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