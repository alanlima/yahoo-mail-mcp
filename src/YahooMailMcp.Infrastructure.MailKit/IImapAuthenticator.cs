using MailKit.Net.Imap;

namespace YahooMailMcp.Infrastructure.MailKit;

public interface IImapAuthenticator
{
    Task AuthenticateAsync(ImapClient client, CancellationToken cancellationToken);
}