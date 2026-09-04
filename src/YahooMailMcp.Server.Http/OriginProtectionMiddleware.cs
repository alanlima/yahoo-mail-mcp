using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Options;

namespace YahooMailMcp.Server.Http;

public sealed partial class OriginProtectionMiddleware(
    RequestDelegate next,
    IOptions<OriginProtectionOptions> options,
    ILogger<OriginProtectionMiddleware> logger)
{
    private static readonly string[] HealthPaths = ["/health/live", "/health/ready"];
    private readonly OriginProtectionOptions options = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!options.Enabled || IsHealthProbe(context.Request.Path))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (!HasValidOriginHeader(context.Request))
        {
            LogRejectedOriginRequest(logger);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        // The verification value is only needed at this boundary. Remove it before
        // authentication, diagnostics, and endpoint code can observe it.
        context.Request.Headers.Remove(options.HeaderName);
        await next(context).ConfigureAwait(false);
    }

    private bool HasValidOriginHeader(HttpRequest request)
    {
        if (request.Headers[options.HeaderName] is not { Count: 1 } values)
        {
            return false;
        }

        var actual = Encoding.UTF8.GetBytes(values[0] ?? string.Empty);
        var expected = Encoding.UTF8.GetBytes(options.HeaderValue!);
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static bool IsHealthProbe(PathString path) => HealthPaths.Any(
        healthPath => string.Equals(path.Value, healthPath, StringComparison.OrdinalIgnoreCase));

    [LoggerMessage(1, LogLevel.Warning, "Rejected request that did not pass origin verification.")]
    private static partial void LogRejectedOriginRequest(ILogger logger);
}