using YahooMailMcp.Application;
using YahooMailMcp.Domain;

namespace YahooMailMcp.UnitTests;

public sealed class DestinationFolderPolicyTests
{
    private readonly DestinationFolderPolicy policy = new(["Trash", "Bin", "Deleted Items"]);
    private readonly MailMessageId sourceId = new("INBOX", 42, 7);

    [Fact]
    public void SpecialUseTrashDestinationIsRejected()
    {
        var destination = Folder("Provider Bin", MailFolderAttributes.Trash);

        var exception = Assert.Throws<MailGatewayException>(() => policy.Validate(sourceId, destination));

        Assert.Equal(MailErrorCodes.TrashDestinationForbidden, exception.Code);
    }

    [Theory]
    [InlineData("Trash", '/')]
    [InlineData("Folders/Trash", '/')]
    [InlineData("Folders.Trash", '.')]
    [InlineData(" deleted items ", '/')]
    public void CanonicalDeniedDestinationIsRejected(string fullName, char delimiter)
    {
        var destination = Folder(fullName, delimiter: delimiter);

        var exception = Assert.Throws<MailGatewayException>(() => policy.Validate(sourceId, destination));

        Assert.Equal(MailErrorCodes.TrashDestinationForbidden, exception.Code);
    }

    [Fact]
    public void SameFolderDestinationIsRejected()
    {
        var destination = Folder("inbox");

        var exception = Assert.Throws<MailGatewayException>(() => policy.Validate(sourceId, destination));

        Assert.Equal(MailErrorCodes.InvalidRequest, exception.Code);
    }

    [Fact]
    public void NormalDestinationIsAccepted()
    {
        policy.Validate(sourceId, Folder("Archive/2026"));
    }

    private static MailFolderInfo Folder(
        string fullName,
        MailFolderAttributes attributes = MailFolderAttributes.None,
        char delimiter = '/') =>
        new(fullName, fullName, delimiter, attributes, null, null, IsSubscribed: true);
}