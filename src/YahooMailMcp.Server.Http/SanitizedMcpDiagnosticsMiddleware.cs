using System.Diagnostics;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Options;

using YahooMailMcp.Mcp;

namespace YahooMailMcp.Server.Http;

public sealed partial class SanitizedMcpDiagnosticsMiddleware(
    RequestDelegate next,
    ILogger<SanitizedMcpDiagnosticsMiddleware> logger,
    IOptions<McpDiagnosticsOptions> diagnosticsOptions,
    IOptions<McpEndpointOptions> endpointOptions)
{
    private static readonly HashSet<string> AllowedMethods =
    [
        "initialize",
        "notifications/initialized",
        "ping",
        "tools/call",
        "tools/list"
    ];

    private static readonly HashSet<string> AllowedToolNames =
    [
        YahooMailTools.GetMessageToolName,
        YahooMailTools.ListFoldersToolName,
        YahooMailTools.ListMessagesToolName,
        YahooMailTools.MarkReadToolName,
        YahooMailTools.MarkUnreadToolName,
        YahooMailTools.MoveMessageToolName,
        YahooMailTools.SearchMessagesToolName,
        YahooMailTools.SetFlaggedToolName,
        YahooMailTools.StatusToolName
    ];

    private static readonly HashSet<string> AllowedArgumentNames =
    [
        "beforeUtc",
        "bodyMode",
        "cursor",
        "destinationFolder",
        "flagged",
        "flaggedOnly",
        "folder",
        "from",
        "hasAttachments",
        "limit",
        "sinceUtc",
        "sourceFolder",
        "subject",
        "text",
        "to",
        "uid",
        "uidValidity",
        "unreadOnly"
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        var options = diagnosticsOptions.Value;
        if (!options.EnableSanitizedPayloadLogging
            || !context.Request.Path.StartsWithSegments(endpointOptions.Value.Path))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var metadata = await ReadMetadataAsync(
            context.Request,
            options.MaximumInspectedRequestBytes,
            context.RequestAborted).ConfigureAwait(false);
        Activity.Current?.SetTag("rpc.method", metadata.Method);
        Activity.Current?.SetTag("tool.name", metadata.ToolName);
        Activity.Current?.SetTag("mcp.argument.keys", metadata.ArgumentNames);
        LogSanitizedRequest(
            logger,
            metadata.Method,
            metadata.ToolName,
            metadata.ArgumentNames,
            context.Request.ContentLength ?? 0);

        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            var durationMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            LogSanitizedResponse(
                logger,
                metadata.Method,
                metadata.ToolName,
                context.Response.StatusCode,
                durationMs);
        }
    }

    private static async Task<McpRequestMetadata> ReadMetadataAsync(
        HttpRequest request,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength is 0 or null
            || request.ContentLength > maximumBytes
            || request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
        {
            return McpRequestMetadata.Omitted;
        }

        request.EnableBuffering();
        try
        {
            using var reader = new StreamReader(
                request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1_024,
                leaveOpen: true);
            var buffer = new char[maximumBytes + 1];
            var charactersRead = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (charactersRead > maximumBytes)
            {
                return McpRequestMetadata.Omitted;
            }

            using var document = JsonDocument.Parse(buffer.AsMemory(0, charactersRead));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return McpRequestMetadata.Omitted;
            }

            var root = document.RootElement;
            var method = SafeMethod(root);
            var toolName = SafeToolName(root);
            var argumentNames = SafeArgumentNames(root);
            return new McpRequestMetadata(method, toolName, argumentNames);
        }
        catch (JsonException)
        {
            return McpRequestMetadata.Omitted;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    private static string SafeMethod(JsonElement root)
    {
        var value = root.TryGetProperty("method", out var method) ? method.GetString() : null;
        return value is not null && AllowedMethods.Contains(value) ? value : "unknown";
    }

    private static string SafeToolName(JsonElement root)
    {
        var value = root.TryGetProperty("params", out var parameters)
            && parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("name", out var name)
            ? name.GetString()
            : null;
        return value is not null && AllowedToolNames.Contains(value) ? value : "none";
    }

    private static string SafeArgumentNames(JsonElement root)
    {
        if (!root.TryGetProperty("params", out var parameters)
            || parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("arguments", out var arguments)
            || arguments.ValueKind != JsonValueKind.Object)
        {
            return "none";
        }

        var names = arguments
            .EnumerateObject()
            .Select(property => property.Name)
            .Where(AllowedArgumentNames.Contains)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return names.Length == 0 ? "none" : string.Join(',', names);
    }

    [LoggerMessage(
        EventId = 3100,
        Level = LogLevel.Debug,
        Message = "Sanitized MCP request; rpc.method={RpcMethod} tool.name={ToolName} argument.keys={ArgumentNames} request.bytes={RequestBytes}. Payload values are omitted.")]
    private static partial void LogSanitizedRequest(
        ILogger logger,
        string rpcMethod,
        string toolName,
        string argumentNames,
        long requestBytes);

    [LoggerMessage(
        EventId = 3101,
        Level = LogLevel.Debug,
        Message = "Sanitized MCP response; rpc.method={RpcMethod} tool.name={ToolName} http.status_code={StatusCode} duration.ms={DurationMs}. Response content is omitted.")]
    private static partial void LogSanitizedResponse(
        ILogger logger,
        string rpcMethod,
        string toolName,
        int statusCode,
        double durationMs);

    private sealed record McpRequestMetadata(string Method, string ToolName, string ArgumentNames)
    {
        public static McpRequestMetadata Omitted { get; } = new("unknown", "none", "none");
    }
}