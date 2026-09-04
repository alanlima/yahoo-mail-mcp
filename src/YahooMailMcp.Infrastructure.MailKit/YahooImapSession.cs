using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using YahooMailMcp.Domain;

namespace YahooMailMcp.Infrastructure.MailKit;

public sealed class YahooImapSession : IAsyncDisposable
{
    private readonly SemaphoreSlim operationLock = new(1, 1);
    private readonly ImapClient client = new();
    private readonly IImapAuthenticator authenticator;
    private readonly YahooOptions options;
    private readonly ILogger<YahooImapSession> logger;
    private readonly TimeProvider timeProvider;
    private DateTimeOffset lastOperationUtc = DateTimeOffset.MinValue;
    private YahooImapFeatureSet features = YahooImapFeatureSet.None;

    public YahooImapSession(
        IImapAuthenticator authenticator,
        IOptions<YahooOptions> options,
        ILogger<YahooImapSession> logger,
        TimeProvider? timeProvider = null)
    {
        this.authenticator = authenticator;
        this.options = options.Value;
        this.logger = logger;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        client.Timeout = checked(this.options.CommandTimeoutSeconds * 1_000);
    }

    public bool IsConnected => client.IsConnected;

    public bool IsAuthenticated => client.IsAuthenticated;

    public YahooImapFeatureSet Features => Volatile.Read(ref features);

    public async Task<T> ExecuteReadAsync<T>(
        string operationName,
        Func<ImapClient, YahooImapFeatureSet, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        using var operationScope = ImapTelemetry.StartOperation(operationName);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
                    var result = await operation(client, Features, cancellationToken).ConfigureAwait(false);
                    lastOperationUtc = timeProvider.GetUtcNow();
                    operationScope.Complete("success");
                    return result;
                }
                catch (OperationCanceledException)
                {
                    operationScope.Complete("cancelled");
                    throw;
                }
                catch (Exception exception) when (IsTransient(exception) && attempt < options.ReadRetryCount)
                {
                    logger.LogReadOperationRetrying(attempt + 1);
                    ImapTelemetry.RecordReconnect("transient_read_failure");
                    await ResetConnectionAsync().ConfigureAwait(false);
                }
                catch (MailGatewayException exception)
                {
                    operationScope.Complete("error", exception.Code);
                    throw;
                }
                catch (TimeoutException exception)
                {
                    operationScope.Complete("error", MailErrorCodes.ConnectionTimeout);
                    throw new MailGatewayException(
                        MailErrorCodes.ConnectionTimeout,
                        "The Yahoo operation timed out.",
                        retryable: true,
                        exception);
                }
                catch (Exception exception)
                {
                    operationScope.Complete("error", MailErrorCodes.ProviderError);
                    throw new MailGatewayException(
                        MailErrorCodes.ProviderError,
                        "Yahoo could not complete the mail operation.",
                        retryable: false,
                        exception);
                }
            }
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<T> ExecuteMutationAsync<T>(
        string operationName,
        Func<ImapClient, YahooImapFeatureSet, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        using var operationScope = ImapTelemetry.StartOperation(operationName);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
                var result = await operation(client, Features, cancellationToken).ConfigureAwait(false);
                lastOperationUtc = timeProvider.GetUtcNow();
                operationScope.Complete("success");
                return result;
            }
            catch (OperationCanceledException)
            {
                operationScope.Complete("cancelled");
                throw;
            }
            catch (MailGatewayException exception)
            {
                operationScope.Complete("error", exception.Code);
                throw;
            }
            catch (Exception exception) when (IsTransient(exception))
            {
                operationScope.Complete("error", MailErrorCodes.ServiceUnavailable);
                ImapTelemetry.RecordReconnect("transient_mutation_failure");
                await ResetConnectionAsync().ConfigureAwait(false);
                throw new MailGatewayException(
                    MailErrorCodes.ServiceUnavailable,
                    "The Yahoo connection was interrupted. The operation was not retried.",
                    retryable: false,
                    exception);
            }
            catch (Exception exception)
            {
                operationScope.Complete("error", MailErrorCodes.ProviderError);
                throw new MailGatewayException(
                    MailErrorCodes.ProviderError,
                    "Yahoo could not complete the mail operation.",
                    retryable: false,
                    exception);
            }
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await operationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await ResetConnectionAsync().ConfigureAwait(false);
            client.Dispose();
        }
        finally
        {
            operationLock.Release();
            operationLock.Dispose();
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (client.IsConnected && client.IsAuthenticated)
        {
            if (timeProvider.GetUtcNow() - lastOperationUtc > TimeSpan.FromSeconds(options.ConnectionIdleTimeoutSeconds))
            {
                await client.NoOpAsync(cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        await ResetConnectionAsync().ConfigureAwait(false);
        var socketOptions = options.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None;
        try
        {
            await client.ConnectAsync(options.Host, options.Port, socketOptions, cancellationToken).ConfigureAwait(false);
            await authenticator.AuthenticateAsync(client, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref features, YahooImapFeatureProjection.FromCapabilities(client.Capabilities));
            lastOperationUtc = timeProvider.GetUtcNow();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MailGatewayException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new MailGatewayException(
                MailErrorCodes.ConnectionFailed,
                "The service could not connect to Yahoo Mail.",
                retryable: true,
                exception);
        }
    }

    private async Task ResetConnectionAsync()
    {
        if (client.IsConnected)
        {
            try
            {
                await client.DisconnectAsync(quit: true).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or ImapProtocolException or ServiceNotConnectedException)
            {
                logger.LogDisconnectFailed();
            }
        }

        Volatile.Write(ref features, YahooImapFeatureSet.None);
    }

    private static bool IsTransient(Exception exception) =>
        exception is IOException
            or TimeoutException
            or ImapProtocolException
            or ServiceNotConnectedException
            or ServiceNotAuthenticatedException;
}

internal static partial class SessionLoggingExtensions
{
    extension(ILogger logger)
    {
        internal void LogReadOperationRetrying(int attempt) => WriteReadOperationRetrying(logger, attempt);

        internal void LogDisconnectFailed() => WriteDisconnectFailed(logger);
    }

    [LoggerMessage(EventId = 2000, Level = LogLevel.Warning, Message = "Retrying a read-only IMAP operation; attempt={Attempt}.")]
    private static partial void WriteReadOperationRetrying(ILogger logger, int attempt);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Debug, Message = "IMAP disconnect did not complete cleanly.")]
    private static partial void WriteDisconnectFailed(ILogger logger);
}