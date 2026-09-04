using MailKit.Net.Imap;

using YahooMailMcp.Domain;
using YahooMailMcp.Infrastructure.MailKit;

namespace YahooMailMcp.UnitTests;

public sealed class YahooImapFeatureProjectionTests
{
    [Fact]
    public void MinimalYahooProfileLeavesOptionalFeaturesDisabled()
    {
        var features = YahooImapFeatureProjection.FromCapabilities(ImapCapabilities.IMAP4rev1);

        Assert.False(features.NativeMoveSupported);
        Assert.False(features.SpecialUseSupported);
        Assert.False(features.ConditionalSyncSupported);
        Assert.False(features.ServerSortSupported);
        Assert.False(features.ServerThreadSupported);
        Assert.False(features.UidPlusSupported);
    }

    [Fact]
    public void RichYahooProfileProjectsOnlyApprovedStandardFeatures()
    {
        var capabilities = ImapCapabilities.IMAP4rev1
            | ImapCapabilities.Move
            | ImapCapabilities.SpecialUse
            | ImapCapabilities.CondStore
            | ImapCapabilities.Sort
            | ImapCapabilities.Thread
            | ImapCapabilities.UidPlus;

        var features = YahooImapFeatureProjection.FromCapabilities(capabilities);

        Assert.True(features.NativeMoveSupported);
        Assert.True(features.SpecialUseSupported);
        Assert.True(features.ConditionalSyncSupported);
        Assert.True(features.ServerSortSupported);
        Assert.True(features.ServerThreadSupported);
        Assert.True(features.UidPlusSupported);
    }

    [Fact]
    public void GmailExtensionDoesNotEnableAnyYahooFeature()
    {
        var capabilities = ImapCapabilities.IMAP4rev1 | ImapCapabilities.GMailExt1;

        var features = YahooImapFeatureProjection.FromCapabilities(capabilities);

        Assert.Equal(YahooImapFeatureSet.None, features);
    }
}