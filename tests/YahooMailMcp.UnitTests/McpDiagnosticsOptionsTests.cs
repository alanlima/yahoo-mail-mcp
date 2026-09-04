using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

using YahooMailMcp.Server.Http;

namespace YahooMailMcp.UnitTests;

public sealed class McpDiagnosticsOptionsTests
{
    [Fact]
    public void SanitizedPayloadLoggingIsAcceptedInDevelopment()
    {
        var validator = new McpDiagnosticsOptionsValidator(new TestHostEnvironment(Environments.Development));

        var result = validator.Validate(
            name: null,
            new McpDiagnosticsOptions { EnableSanitizedPayloadLogging = true });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void SanitizedPayloadLoggingIsRejectedOutsideDevelopment()
    {
        var validator = new McpDiagnosticsOptionsValidator(new TestHostEnvironment(Environments.Production));

        var result = validator.Validate(
            name: null,
            new McpDiagnosticsOptions { EnableSanitizedPayloadLogging = true });

        Assert.True(result.Failed);
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "YahooMailMcp.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}