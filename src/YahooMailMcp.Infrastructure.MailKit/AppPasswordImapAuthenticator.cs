using MailKit.Net.Imap;

using Microsoft.Extensions.Options;

using YahooMailMcp.Domain;

namespace YahooMailMcp.Infrastructure.MailKit;

public sealed class AppPasswordImapAuthenticator : IImapAuthenticator
{
    private readonly string email;
    private readonly string appPassword;
    public AppPasswordImapAuthenticator(IOptions<YahooOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        email = options.Value.Email ?? throw new InvalidOperationException("Validated Yahoo email is unavailable.");
        appPassword = options.Value.AppPassword ?? throw new InvalidOperationException("Validated Yahoo app password is unavailable.");
    }

    public async Task AuthenticateAsync(ImapClient client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        try
        {
            await client.AuthenticateAsync(email, appPassword, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            ImapTelemetry.RecordAuthenticationFailure();
            throw new MailGatewayException(
                MailErrorCodes.AuthenticationFailed,
                "Yahoo authentication failed.",
                retryable: false,
                exception);
        }
    }
}