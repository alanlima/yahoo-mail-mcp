using System.Text.Json.Serialization;

namespace YahooMailMcp.Mcp;

public sealed record MailToolError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("retryable")] bool Retryable);

public sealed record MailToolResult<T>(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("data")] T? Data,
    [property: JsonPropertyName("error")] MailToolError? Error);