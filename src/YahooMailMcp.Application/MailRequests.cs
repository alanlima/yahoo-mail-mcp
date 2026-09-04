using System.Text.Json.Serialization;

using YahooMailMcp.Domain;

namespace YahooMailMcp.Application;

public sealed record ListMessagesRequest(
    [property: JsonPropertyName("folder")] string Folder,
    [property: JsonPropertyName("limit")] int Limit = 10,
    [property: JsonPropertyName("cursor")] string? Cursor = null);

public sealed record SearchMessagesRequest(
    [property: JsonPropertyName("folder")] string Folder,
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("from")] string? From = null,
    [property: JsonPropertyName("to")] string? To = null,
    [property: JsonPropertyName("subject")] string? Subject = null,
    [property: JsonPropertyName("sinceUtc")] DateTimeOffset? SinceUtc = null,
    [property: JsonPropertyName("beforeUtc")] DateTimeOffset? BeforeUtc = null,
    [property: JsonPropertyName("unreadOnly")] bool UnreadOnly = false,
    [property: JsonPropertyName("flaggedOnly")] bool FlaggedOnly = false,
    [property: JsonPropertyName("hasAttachments")] bool? HasAttachments = null,
    [property: JsonPropertyName("limit")] int Limit = 10,
    [property: JsonPropertyName("cursor")] string? Cursor = null);

public sealed record GetMessageRequest(
    [property: JsonPropertyName("id")] MailMessageId Id,
    [property: JsonPropertyName("bodyMode")] MessageBodyMode BodyMode = MessageBodyMode.Text);

public sealed record PagedResult<T>(
    [property: JsonPropertyName("items")] IReadOnlyList<T> Items,
    [property: JsonPropertyName("nextCursor")] string? NextCursor);

public sealed record MailLimits(
    int DefaultPageSize = 10,
    int MaxPageSize = 50,
    int MaxBodyCharacters = 50_000,
    int HardMaxBodyCharacters = 200_000)
{
    public MailLimits Validate()
    {
        if (DefaultPageSize is < 1 || DefaultPageSize > MaxPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(DefaultPageSize));
        }

        if (MaxPageSize is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPageSize));
        }

        if (MaxBodyCharacters is < 1 || MaxBodyCharacters > HardMaxBodyCharacters)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxBodyCharacters));
        }

        if (HardMaxBodyCharacters is < 1 or > 200_000)
        {
            throw new ArgumentOutOfRangeException(nameof(HardMaxBodyCharacters));
        }

        return this;
    }
}

public static class MailRequestValidator
{
    public static void Validate(ListMessagesRequest request, MailLimits limits)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateFolder(request.Folder);
        ValidateLimit(request.Limit, limits);
    }

    public static void Validate(SearchMessagesRequest request, MailLimits limits)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateFolder(request.Folder);
        ValidateLimit(request.Limit, limits);

        if (request.SinceUtc is not null && request.BeforeUtc is not null && request.SinceUtc >= request.BeforeUtc)
        {
            throw InvalidRequest("sinceUtc must be earlier than beforeUtc.");
        }

        if (string.IsNullOrWhiteSpace(request.Text)
            && string.IsNullOrWhiteSpace(request.From)
            && string.IsNullOrWhiteSpace(request.To)
            && string.IsNullOrWhiteSpace(request.Subject)
            && request.SinceUtc is null
            && request.BeforeUtc is null
            && !request.UnreadOnly
            && !request.FlaggedOnly
            && request.HasAttachments is null)
        {
            throw InvalidRequest("At least one structured search filter is required.");
        }
    }

    public static void Validate(GetMessageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateFolder(request.Id.Folder);

        if (request.Id.Uid == 0)
        {
            throw InvalidRequest("Message UID must be greater than zero.");
        }
    }

    public static void Validate(SetReadStateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMessageId(request.Id);
    }

    public static void Validate(SetFlaggedStateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMessageId(request.Id);
    }

    public static void Validate(MoveMessageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMessageId(request.SourceId);
        ValidateFolder(request.DestinationFolder);
    }

    private static void ValidateMessageId(MailMessageId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        ValidateFolder(id.Folder);

        if (id.Uid == 0)
        {
            throw InvalidRequest("Message UID must be greater than zero.");
        }
    }

    private static void ValidateFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            throw InvalidRequest("A folder is required.");
        }
    }

    private static void ValidateLimit(int limit, MailLimits limits)
    {
        limits.Validate();
        if (limit < 1)
        {
            throw InvalidRequest("Limit must be greater than zero.");
        }

        if (limit > limits.MaxPageSize)
        {
            throw new MailGatewayException(
                MailErrorCodes.ResultLimitExceeded,
                $"Limit must not exceed {limits.MaxPageSize}.",
                retryable: false);
        }
    }

    private static MailGatewayException InvalidRequest(string message) =>
        new(MailErrorCodes.InvalidRequest, message, retryable: false);
}