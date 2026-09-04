using System.Text.Json.Serialization;

namespace YahooMailMcp.Domain;

/// <summary>
/// Identifies a message within an IMAP folder and UID validity epoch.
/// </summary>
/// <param name="Folder">The provider folder full name.</param>
/// <param name="Uid">The message UID within the folder.</param>
/// <param name="UidValidity">The folder UID validity value, when known.</param>
public sealed record MailMessageId(
    [property: JsonPropertyName("folder")] string Folder,
    [property: JsonPropertyName("uid")] uint Uid,
    [property: JsonPropertyName("uidValidity")] uint? UidValidity);