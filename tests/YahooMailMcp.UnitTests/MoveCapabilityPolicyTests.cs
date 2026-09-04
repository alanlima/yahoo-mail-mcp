using YahooMailMcp.Application;
using YahooMailMcp.Domain;

namespace YahooMailMcp.UnitTests;

public sealed class MoveCapabilityPolicyTests
{
    [Fact]
    public void MissingNativeMoveIsRejected()
    {
        var exception = Assert.Throws<MailGatewayException>(() =>
            MoveCapabilityPolicy.RequireNativeMove(YahooImapFeatureSet.None));

        Assert.Equal(MailErrorCodes.OperationNotSupported, exception.Code);
        Assert.False(exception.Retryable);
    }

    [Fact]
    public void AdvertisedNativeMoveIsAccepted()
    {
        var features = YahooImapFeatureSet.None with { NativeMoveSupported = true };

        MoveCapabilityPolicy.RequireNativeMove(features);
    }
}