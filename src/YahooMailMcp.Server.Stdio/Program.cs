using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModelContextProtocol.Server;

using YahooMailMcp.Infrastructure.MailKit;
using YahooMailMcp.Mcp;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.AddYahooMailInfrastructure();
builder.Services.AddYahooMailMcpTelemetry();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(typeof(YahooMailTools).Assembly);

await builder.Build().RunAsync().ConfigureAwait(false);