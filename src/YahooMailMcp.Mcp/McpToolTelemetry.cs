using System.Diagnostics;
using System.Diagnostics.Metrics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using YahooMailMcp.Domain;

namespace YahooMailMcp.Mcp;

public sealed partial class McpToolTelemetry(ILogger<McpToolTelemetry> logger)
{
    public const string InstrumentationName = "YahooMailMcp.Mcp";

    private static readonly ActivitySource ActivitySource = new(InstrumentationName);
    private static readonly Meter Meter = new(InstrumentationName);
    private static readonly Counter<long> ToolCalls = Meter.CreateCounter<long>("yahoo_mail_mcp_tool_calls_total");
    private static readonly Histogram<double> ToolDuration = Meter.CreateHistogram<double>(
        "yahoo_mail_mcp_tool_duration_ms",
        unit: "ms");
    private static readonly UpDownCounter<long> ActiveRequests = Meter.CreateUpDownCounter<long>(
        "yahoo_mail_mcp_active_requests");

    public async Task<MailToolResult<T>> InvokeAsync<T>(string toolName, Func<Task<T>> operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(operation);

        var startedAt = Stopwatch.GetTimestamp();
        var tags = new TagList { { "tool.name", toolName } };
        ActiveRequests.Add(1, tags);
        using var activity = ActivitySource.StartActivity(toolName, ActivityKind.Internal);
        activity?.SetTag("tool.name", toolName);
        LogToolStarted(logger, toolName);

        try
        {
            var data = await operation().ConfigureAwait(false);
            RecordCompletion(toolName, "success", errorCode: null, startedAt, activity);
            return new MailToolResult<T>(Success: true, data, null);
        }
        catch (MailGatewayException exception)
        {
            RecordCompletion(toolName, "error", exception.Code, startedAt, activity);
            return new MailToolResult<T>(
                Success: false,
                default,
                new MailToolError(exception.Code, exception.Message, exception.Retryable));
        }
        catch
        {
            RecordCompletion(toolName, "internal_error", MailErrorCodes.InternalError, startedAt, activity);
            throw;
        }
        finally
        {
            ActiveRequests.Add(-1, tags);
        }
    }

    private void RecordCompletion(
        string toolName,
        string outcome,
        string? errorCode,
        long startedAt,
        Activity? activity)
    {
        var durationMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        var tags = new TagList
        {
            { "tool.name", toolName },
            { "outcome", outcome }
        };
        ToolCalls.Add(1, tags);
        ToolDuration.Record(durationMs, tags);
        activity?.SetTag("outcome", outcome);
        activity?.SetTag("error.code", errorCode);
        activity?.SetStatus(
            outcome == "success" ? ActivityStatusCode.Ok : ActivityStatusCode.Error,
            errorCode);
        LogToolCompleted(logger, toolName, outcome, durationMs, errorCode ?? "none");
    }

    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Debug,
        Message = "MCP tool call started; tool.name={ToolName}.")]
    private static partial void LogToolStarted(ILogger logger, string toolName);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "MCP tool call completed; tool.name={ToolName} outcome={Outcome} duration.ms={DurationMs} error.code={ErrorCode}.")]
    private static partial void LogToolCompleted(
        ILogger logger,
        string toolName,
        string outcome,
        double durationMs,
        string errorCode);
}

public static class McpServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddYahooMailMcpTelemetry()
        {
            services.AddSingleton<McpToolTelemetry>();
            return services;
        }
    }
}