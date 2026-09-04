using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace YahooMailMcp.Infrastructure.MailKit;

public static class ImapTelemetry
{
    public const string InstrumentationName = "YahooMailMcp.Infrastructure.MailKit";

    private static readonly ActivitySource ActivitySource = new(InstrumentationName);
    private static readonly Meter Meter = new(InstrumentationName);
    private static readonly Counter<long> Operations = Meter.CreateCounter<long>(
        "yahoo_mail_mcp_imap_operations_total");
    private static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(
        "yahoo_mail_mcp_imap_operation_duration_ms",
        unit: "ms");
    private static readonly Counter<long> Reconnects = Meter.CreateCounter<long>(
        "yahoo_mail_mcp_reconnects_total");
    private static readonly Counter<long> AuthenticationFailures = Meter.CreateCounter<long>(
        "yahoo_mail_mcp_auth_failures_total");

    public static OperationScope StartOperation(string operationName) =>
        new(operationName, ActivitySource, Operations, OperationDuration);

    public static void RecordReconnect(string reason) =>
        Reconnects.Add(1, new TagList { { "reason", reason } });

    public static void RecordAuthenticationFailure() => AuthenticationFailures.Add(1);

    public sealed class OperationScope : IDisposable
    {
        private readonly string operationName;
        private readonly long startedAt = Stopwatch.GetTimestamp();
        private readonly Activity? activity;
        private bool completed;

        private readonly Counter<long> operations;
        private readonly Histogram<double> operationDuration;

        internal OperationScope(
            string operationName,
            ActivitySource activitySource,
            Counter<long> operations,
            Histogram<double> operationDuration)
        {
            this.operationName = operationName;
            this.operations = operations;
            this.operationDuration = operationDuration;
            activity = activitySource.StartActivity(operationName, ActivityKind.Client);
            activity?.SetTag("operation.name", operationName);
        }

        public void Complete(string outcome, string? errorCode = null)
        {
            if (completed)
            {
                return;
            }

            completed = true;
            var tags = new TagList
            {
                { "operation.name", operationName },
                { "outcome", outcome }
            };
            operations.Add(1, tags);
            operationDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, tags);
            activity?.SetTag("outcome", outcome);
            activity?.SetTag("error.code", errorCode);
            activity?.SetStatus(outcome == "success" ? ActivityStatusCode.Ok : ActivityStatusCode.Error, errorCode);
        }

        public void Dispose()
        {
            Complete("internal_error");
            activity?.Dispose();
        }
    }
}