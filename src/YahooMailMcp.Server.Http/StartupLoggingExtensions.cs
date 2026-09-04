namespace YahooMailMcp.Server.Http;

internal static partial class StartupLoggingExtensions
{
    extension(ILogger logger)
    {
        internal void LogSafetyCapabilities() => WriteSafetyCapabilities(logger);
    }

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "mail.delete_capability=false")]
    private static partial void WriteSafetyCapabilities(ILogger logger);
}