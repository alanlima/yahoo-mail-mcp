using System.Text;

using Microsoft.Extensions.Options;

namespace YahooMailMcp.Server.Http;

public sealed class McpEndpointOptions
{
    public const string SectionName = "Mcp";

    public string Path { get; set; } = "/mcp";

    public string AuthenticationMode { get; set; } = "ApiKeyAndOAuth";

    // Retain the original configuration key so existing Key Vault references and rotations remain valid.
    public string? BearerToken { get; set; }

    public int RequestsPerMinute { get; set; } = 60;
}

public sealed class McpEndpointOptionsValidator : IValidateOptions<McpEndpointOptions>
{
    public ValidateOptionsResult Validate(string? name, McpEndpointOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (!options.Path.StartsWith('/') || options.Path.Any(char.IsWhiteSpace))
        {
            failures.Add("MCP path must be an absolute path without whitespace.");
        }

        if (!options.AuthenticationMode.Equals("ApiKeyAndOAuth", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("MCP authentication mode must be ApiKeyAndOAuth.");
        }

        if (string.IsNullOrWhiteSpace(options.BearerToken)
            || Encoding.UTF8.GetByteCount(options.BearerToken) < 32)
        {
            failures.Add("MCP API key must contain at least 32 UTF-8 bytes.");
        }

        if (options.RequestsPerMinute is < 1 or > 10_000)
        {
            failures.Add("MCP requests per minute must be between 1 and 10000.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}