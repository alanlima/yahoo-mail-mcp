using YahooMailMcp.Server.Http;

namespace YahooMailMcp.UnitTests;

public sealed class McpOAuthOptionsTests
{
    private static readonly McpOAuthOptions ValidOptions = new()
    {
        Enabled = true,
        TenantId = "b16c65ab-9711-44cd-abf1-24e6180a4609",
        ClientId = "7553b3c9-f602-42a8-b5fe-8a54af0dd3af",
        Audience = "7553b3c9-f602-42a8-b5fe-8a54af0dd3af",
        Resource = "https://mcp.alanlima.cloud/mcp",
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