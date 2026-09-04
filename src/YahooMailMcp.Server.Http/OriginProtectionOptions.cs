using System.Text;

using Microsoft.Extensions.Options;

namespace YahooMailMcp.Server.Http;

public sealed class OriginProtectionOptions
{
    public const string SectionName = "OriginProtection";

    public bool Enabled { get; set; }

    public string HeaderName { get; set; } = "X-Origin-Verify";

    public string? HeaderValue { get; set; }
}

public sealed class OriginProtectionOptionsValidator : IValidateOptions<OriginProtectionOptions>
{
    public ValidateOptionsResult Validate(string? name, OriginProtectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.HeaderName) || !options.HeaderName.All(IsHeaderTokenCharacter))
        {
            failures.Add("Origin-protection header name must be a valid HTTP header token.");
        }

        if (string.IsNullOrWhiteSpace(options.HeaderValue)
            || Encoding.UTF8.GetByteCount(options.HeaderValue) < 32
            || options.HeaderValue.Contains('\r', StringComparison.Ordinal)
            || options.HeaderValue.Contains('\n', StringComparison.Ordinal))
        {
            failures.Add("Origin-protection header value must contain at least 32 UTF-8 bytes and no line breaks.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsHeaderTokenCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value)
        || value is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';
}