using Microsoft.Extensions.Options;

namespace YahooMailMcp.Infrastructure.MailKit;

public sealed class MailRuntimeOptions
{
    public const string SectionName = "Mail";

    public int DefaultPageSize { get; set; } = 10;

    public int MaxPageSize { get; set; } = 50;

    public int MaxBodyCharacters { get; set; } = 50_000;

    public int HardMaxBodyCharacters { get; set; } = 200_000;
}

public sealed class CursorOptions
{
    public const string SectionName = "Cursor";

    public string? SigningKey { get; set; }

    public int MaximumAgeMinutes { get; set; } = 1_440;
}

public sealed class CursorOptionsValidator : IValidateOptions<CursorOptions>
{
    public ValidateOptionsResult Validate(string? name, CursorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.SigningKey)
            || System.Text.Encoding.UTF8.GetByteCount(options.SigningKey) < 32)
        {
            return ValidateOptionsResult.Fail("Cursor signing key must contain at least 32 UTF-8 bytes.");
        }

        return options.MaximumAgeMinutes is >= 1 and <= 10_080
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Cursor maximum age must be between 1 and 10080 minutes.");
    }
}