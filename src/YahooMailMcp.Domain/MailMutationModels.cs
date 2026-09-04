using System.Text.Json.Serialization;

namespace YahooMailMcp.Domain;

public sealed record MessageStateResult(
    [property: JsonPropertyName("id")] MailMessageId Id,
    [property: JsonPropertyName("isRead")] bool? IsRead,
    [property: JsonPropertyName("isFlagged")] bool? IsFlagged);

public sealed record MoveMessageResult(
    [property: JsonPropertyName("sourceId")] MailMessageId SourceId,
    [property: JsonPropertyName("destinationId")] MailMessageId? DestinationId,
    [property: JsonPropertyName("completed")] bool Completed);