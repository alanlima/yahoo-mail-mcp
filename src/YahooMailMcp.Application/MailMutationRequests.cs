using System.Text.Json.Serialization;

using YahooMailMcp.Domain;

namespace YahooMailMcp.Application;

public sealed record SetReadStateRequest(
    [property: JsonPropertyName("id")] MailMessageId Id,
    [property: JsonPropertyName("isRead")] bool IsRead);

public sealed record SetFlaggedStateRequest(
    [property: JsonPropertyName("id")] MailMessageId Id,
    [property: JsonPropertyName("isFlagged")] bool IsFlagged);

public sealed record MoveMessageRequest(
    [property: JsonPropertyName("sourceId")] MailMessageId SourceId,
    [property: JsonPropertyName("destinationFolder")] string DestinationFolder);