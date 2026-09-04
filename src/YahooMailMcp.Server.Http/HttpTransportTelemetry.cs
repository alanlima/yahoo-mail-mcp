using System.Diagnostics.Metrics;

namespace YahooMailMcp.Server.Http;

public sealed class HttpTransportTelemetry
{
    public const string InstrumentationName = "YahooMailMcp.Server.Http";

    private static readonly Meter Meter = new(InstrumentationName);
    private static readonly Counter<long> RateLimitRejections = Meter.CreateCounter<long>(
        "yahoo_mail_mcp_rate_limit_rejections_total");

    public void RecordRateLimitRejection() => RateLimitRejections.Add(1);
}