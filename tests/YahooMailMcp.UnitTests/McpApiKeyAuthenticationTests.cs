using YahooMailMcp.Server.Http;

namespace YahooMailMcp.UnitTests;

public sealed class McpApiKeyAuthenticationTests
{
    private const string ValidKey = "0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData(ValidKey, true)]
    [InlineData("0123456789abcdef0123456789abcdeg", false)]
    [InlineData("short", false)]
    [InlineData("", false)]
    public void ApiKeyComparerAcceptsOnlyExactConfiguredKey(string suppliedKey, bool expected)
    {
        Assert.Equal(expected, McpApiKeyComparer.Equals(suppliedKey, ValidKey));
    }

    [Fact]
    public void ApiKeyComparerRejectsMissingConfiguration()
    {
        Assert.False(McpApiKeyComparer.Equals(ValidKey, configuredKey: null));
    }

    [Fact]
    public void EndpointOptionsAcceptSecureConfiguration()
    {
        var result = new McpEndpointOptionsValidator().Validate(
            name: null,
            new McpEndpointOptions { BearerToken = ValidKey });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("mcp", ValidKey)]
    [InlineData("/mcp path", ValidKey)]
    [InlineData("/mcp", "short")]
    public void EndpointOptionsRejectInvalidPathOrKey(string path, string key)
    {
        var result = new McpEndpointOptionsValidator().Validate(
            name: null,
            new McpEndpointOptions { Path = path, BearerToken = key });

        Assert.True(result.Failed);
    }
}