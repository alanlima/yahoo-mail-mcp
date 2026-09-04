using Microsoft.Extensions.Options;

namespace YahooMailMcp.Server.Http;

public sealed class McpOAuthOptions
{
    public const string SectionName = "Mcp:OAuth";

    public bool Enabled { get; set; }

    public string? TenantId { get; set; }

    public string? ClientId { get; set; }

    public string? Audience { get; set; }

    public string? Resource { get; set; }

    public string RequiredScope { get; set; } = "access_as_user";

    public string Authority => $"https://login.microsoftonline.com/{TenantId}/v2.0";

    public string EffectiveAudience => string.IsNullOrWhiteSpace(Audience) ? ClientId! : Audience;

    public string QualifiedScope => $"{Resource!.TrimEnd('/')}/{RequiredScope}";
}

public sealed class McpOAuthOptionsValidator : IValidateOptions<McpOAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, McpOAuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (!Guid.TryParse(options.TenantId, out _))
        {
            failures.Add("MCP OAuth tenant ID must be a GUID.");
        }

        if (!Guid.TryParse(options.ClientId, out _))
        {
            failures.Add("MCP OAuth client ID must be a GUID.");
        }

        if (!Uri.TryCreate(options.Resource, UriKind.Absolute, out var resource)
            || resource.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(resource.Query)
            || !string.IsNullOrEmpty(resource.Fragment))
        {
            failures.Add("MCP OAuth resource must be an absolute HTTPS URI without a query or fragment.");
        }

        if (string.IsNullOrWhiteSpace(options.RequiredScope)
            || options.RequiredScope.Any(char.IsWhiteSpace)
            || options.RequiredScope.Contains('/'))
        {
            failures.Add("MCP OAuth required scope must be one non-empty scope name.");
        }

        if (!Guid.TryParse(options.EffectiveAudience, out _))
        {
            failures.Add("MCP OAuth audience must be the Entra application GUID.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}