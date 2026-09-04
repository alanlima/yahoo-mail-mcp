using MailKit.Net.Imap;

using YahooMailMcp.Domain;

namespace YahooMailMcp.Infrastructure.MailKit;

public static class YahooImapFeatureProjection
{
    public static YahooImapFeatureSet FromCapabilities(ImapCapabilities capabilities) =>
        new(
            NativeMoveSupported: capabilities.HasFlag(ImapCapabilities.Move),
            SpecialUseSupported: capabilities.HasFlag(ImapCapabilities.SpecialUse),
            ConditionalSyncSupported: capabilities.HasFlag(ImapCapabilities.CondStore)
                || capabilities.HasFlag(ImapCapabilities.QuickResync),
            ServerSortSupported: capabilities.HasFlag(ImapCapabilities.Sort),
            ServerThreadSupported: capabilities.HasFlag(ImapCapabilities.Thread),
            UidPlusSupported: capabilities.HasFlag(ImapCapabilities.UidPlus));
}