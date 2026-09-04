using Microsoft.Extensions.Options;

namespace YahooMailMcp.Server.Http;

public sealed class McpDiagnosticsOptions
{
    public const string SectionName = "McpDiagnostics";

    public bool EnableSanitizedPayloadLogging { get; set; }

    public int MaximumInspectedRequestBytes { get; set; } = 65_536;
}

public sealed class McpDiagnosticsOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<McpDiagnosticsOptions>
{
    public ValidateOptionsResult Validate(string? name, McpDiagnosticsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (options.EnableSanitizedPayloadLogging && !environment.IsDevelopment())
        {
            failures.Add("Sanitized MCP payload diagnostics can only be enabled in Development.");
        }

        if (options.MaximumInspectedRequestBytes is < 1_024 or > 1_048_576)
        {
            failures.Add("Maximum inspected MCP request bytes must be between 1024 and 1048576.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}