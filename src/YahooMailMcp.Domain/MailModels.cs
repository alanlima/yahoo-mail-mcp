using System.Text.Json.Serialization;

namespace YahooMailMcp.Domain;

[Flags]
public enum MailFolderAttributes
{
    None = 0,
    Inbox = 1 << 0,
    Drafts = 1 << 1,
    Sent = 1 << 2,
    Junk = 1 << 3,
    Trash = 1 << 4,
    Archive = 1 << 5,
    NoSelect = 1 << 6,
    HasChildren = 1 << 7,
    HasNoChildren = 1 << 8
}

[Flags]
public enum MailMessageState
{
    None = 0,
    Seen = 1 << 0,
    Answered = 1 << 1,
    Flagged = 1 << 2,
    Draft = 1 << 3,
    Recent = 1 << 4
}

public enum MessageBodyMode
{
    Metadata,
    Text,
    HeadersAndText
}

public sealed record MailAddress(
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("address")] string Address);

public sealed record MailAttachmentInfo(
    [property: JsonPropertyName("fileName")] string? FileName,
    [property: JsonPropertyName("mediaType")] string MediaType,
    [property: JsonPropertyName("sizeBytes")] long? SizeBytes,
    [property: JsonPropertyName("contentId")] string? ContentId);

public sealed record YahooImapFeatureSet(
    [property: JsonPropertyName("nativeMoveSupported")] bool NativeMoveSupported,
    [property: JsonPropertyName("specialUseSupported")] bool SpecialUseSupported,
    [property: JsonPropertyName("conditionalSyncSupported")] bool ConditionalSyncSupported,
    [property: JsonPropertyName("serverSortSupported")] bool ServerSortSupported,
    [property: JsonPropertyName("serverThreadSupported")] bool ServerThreadSupported,
    [property: JsonPropertyName("uidPlusSupported")] bool UidPlusSupported)
{
    public static YahooImapFeatureSet None { get; } = new(false, false, false, false, false, false);
}

public sealed record MailAccountStatus(
    [property: JsonPropertyName("connected")] bool Connected,
    [property: JsonPropertyName("authenticated")] bool Authenticated,
    [property: JsonPropertyName("features")] YahooImapFeatureSet Features);

public sealed record MailFolderInfo(
    [property: JsonPropertyName("fullName")] string FullName,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("delimiter")] char? Delimiter,
    [property: JsonPropertyName("attributes")] MailFolderAttributes Attributes,
    [property: JsonPropertyName("unreadCount")] int? UnreadCount,
    [property: JsonPropertyName("totalCount")] int? TotalCount,
    [property: JsonPropertyName("isSubscribed")] bool IsSubscribed);

public sealed record MailMessageSummary(
    [property: JsonPropertyName("id")] MailMessageId Id,
    [property: JsonPropertyName("subject")] string? Subject,
    [property: JsonPropertyName("from")] IReadOnlyList<MailAddress> From,
    [property: JsonPropertyName("to")] IReadOnlyList<MailAddress> To,
    [property: JsonPropertyName("sentAtUtc")] DateTimeOffset? SentAtUtc,
    [property: JsonPropertyName("receivedAtUtc")] DateTimeOffset? ReceivedAtUtc,
    [property: JsonPropertyName("sizeBytes")] long? SizeBytes,
    [property: JsonPropertyName("flags")] MailMessageState Flags,
    [property: JsonPropertyName("hasAttachments")] bool HasAttachments,
    [property: JsonPropertyName("attachments")] IReadOnlyList<MailAttachmentInfo> Attachments);

public sealed record MailMessageDetail(
    [property: JsonPropertyName("summary")] MailMessageSummary Summary,
    [property: JsonPropertyName("headers")] IReadOnlyDictionary<string, string> Headers,
    [property: JsonPropertyName("bodyText")] string? BodyText,
    [property: JsonPropertyName("bodyTruncated")] bool BodyTruncated);