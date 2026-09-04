using YahooMailMcp.Domain;

namespace YahooMailMcp.Application;

public sealed class DestinationFolderPolicy(IEnumerable<string> deniedCanonicalNames)
{
    private readonly HashSet<string> deniedCanonicalNames = deniedCanonicalNames
        .Select(name => Canonicalize(name, null))
        .Where(name => name.Length > 0)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public void Validate(MailMessageId sourceId, MailFolderInfo destination)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        ArgumentNullException.ThrowIfNull(destination);

        var sourceName = Canonicalize(sourceId.Folder, destination.Delimiter);
        var destinationName = Canonicalize(destination.FullName, destination.Delimiter);
        if (sourceName.Equals(destinationName, StringComparison.OrdinalIgnoreCase))
        {
            throw new MailGatewayException(
                MailErrorCodes.InvalidRequest,
                "Source and destination folders must be different.",
                retryable: false);
        }

        var leafName = destinationName.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? destinationName;
        if (destination.Attributes.HasFlag(MailFolderAttributes.Trash)
            || deniedCanonicalNames.Contains(destinationName)
            || deniedCanonicalNames.Contains(leafName))
        {
            throw new MailGatewayException(
                MailErrorCodes.TrashDestinationForbidden,
                "Moving messages to a trash folder is forbidden.",
                retryable: false);
        }
    }

    public static string Canonicalize(string folderName, char? delimiter)
    {
        ArgumentNullException.ThrowIfNull(folderName);
        var normalized = folderName.Trim().Replace('\\', '/');
        if (delimiter is not null and not '/' and not '\\')
        {
            normalized = normalized.Replace(delimiter.Value, '/');
        }

        return string.Join(
            '/',
            normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}