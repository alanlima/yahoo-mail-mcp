using YahooMailMcp.Domain;

namespace YahooMailMcp.Application;

public static class MoveCapabilityPolicy
{
    public static void RequireNativeMove(YahooImapFeatureSet features)
    {
        ArgumentNullException.ThrowIfNull(features);
        if (!features.NativeMoveSupported)
        {
            throw new MailGatewayException(
                MailErrorCodes.OperationNotSupported,
                "Yahoo does not advertise native IMAP MOVE for this session.",
                retryable: false);
        }
    }
}