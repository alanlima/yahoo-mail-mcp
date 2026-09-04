using Microsoft.Extensions.Options;

namespace YahooMailMcp.Infrastructure.MailKit;

public enum YahooAuthenticationMode
{
    AppPassword
}

public sealed class YahooOptions
{
    public const string SectionName = "Yahoo";

    public string Host { get; set; } = "imap.mail.yahoo.com";

    public int Port { get; set; } = 993;

    public bool UseSsl { get; set; } = true;

    public string? Email { get; set; }

    public string? AppPassword { get; set; }

    public YahooAuthenticationMode AuthenticationMode { get; set; } = YahooAuthenticationMode.AppPassword;

    public int ConnectionIdleTimeoutSeconds { get; set; } = 240;

    public int CommandTimeoutSeconds { get; set; } = 30;

    public int ReadRetryCount { get; set; } = 1;

    public string[] TrashFolderDenyList { get; set; } = ["Trash", "Bin", "Deleted Items", "Deleted Messages"];

    public bool AllowNonYahooEndpoint { get; set; }
}

public sealed class YahooOptionsValidator : IValidateOptions<YahooOptions>
{
    public ValidateOptionsResult Validate(string? name, YahooOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            failures.Add("Yahoo host is required.");
        }

        if (options.Port is < 1 or > 65_535)
        {
            failures.Add("Yahoo port must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(options.Email))
        {
            failures.Add("Yahoo email is required.");
        }

        if (string.IsNullOrWhiteSpace(options.AppPassword))
        {
            failures.Add("Yahoo app password is required.");
        }

        if (options.ConnectionIdleTimeoutSeconds is < 30 or > 3_600)
        {
            failures.Add("Yahoo connection idle timeout must be between 30 and 3600 seconds.");
        }

        if (options.CommandTimeoutSeconds is < 5 or > 300)
        {
            failures.Add("Yahoo command timeout must be between 5 and 300 seconds.");
        }

        if (options.ReadRetryCount is < 0 or > 3)
        {
            failures.Add("Yahoo read retry count must be between 0 and 3.");
        }

        if (!options.AllowNonYahooEndpoint
            && (!options.Host.Equals("imap.mail.yahoo.com", StringComparison.OrdinalIgnoreCase)
                || options.Port != 993
                || !options.UseSsl))
        {
            failures.Add("Production Yahoo configuration requires imap.mail.yahoo.com:993 with SSL.");
        }

        if (options.TrashFolderDenyList.Length == 0
            || options.TrashFolderDenyList.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add("Yahoo trash folder deny-list must contain only non-empty names.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}