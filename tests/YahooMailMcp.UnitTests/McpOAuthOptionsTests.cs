using YahooMailMcp.Server.Http;

namespace YahooMailMcp.UnitTests;

public sealed class McpOAuthOptionsTests
{
    private static readonly McpOAuthOptions ValidOptions = new()
    {
        Enabled = true,
        TenantId = "11111111-1111-4111-8111-111111111111",
        ClientId = "22222222-2222-4222-8222-222222222222",
        Audience = "22222222-2222-4222-8222-222222222222",
        Resource = "https://mcp.example.com/mcp",
        RequiredScope = "access_as_user"
    };

    [Fact]
    public void DisabledOAuthDoesNotRequireOAuthSettings()
    {
        var validator = new McpOAuthOptionsValidator();

        var result = validator.Validate(name: null, new McpOAuthOptions { Enabled = false });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void CompleteOAuthConfigurationIsAccepted()
    {
        var validator = new McpOAuthOptionsValidator();

        var result = validator.Validate(name: null, ValidOptions);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void ResourceUrlIsRejectedAsJwtAudience()
    {
        var validator = new McpOAuthOptionsValidator();
        var options = new McpOAuthOptions
        {
            Enabled = true,
            TenantId = ValidOptions.TenantId,
            ClientId = ValidOptions.ClientId,
            Audience = ValidOptions.Resource,
            Resource = ValidOptions.Resource,
            RequiredScope = ValidOptions.RequiredScope
        };

        var result = validator.Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains("application GUID", result.FailureMessage, StringComparison.Ordinal);
    }
}