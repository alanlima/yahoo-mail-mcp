using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace YahooMailMcp.Server.Http;

public sealed partial class McpApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "McpApiKey";
    private const string AuthorizationScheme = "ApiKey";
    private readonly IOptionsMonitor<McpEndpointOptions> endpointOptions;
    private readonly ILogger<McpApiKeyAuthenticationHandler> logger;

    public McpApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder,
        ILogger<McpApiKeyAuthenticationHandler> logger,
        IOptionsMonitor<McpEndpointOptions> endpointOptions)
        : base(options, loggerFactory, encoder)
    {
        this.endpointOptions = endpointOptions;
        this.logger = logger;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (Request.Headers.Authorization.Count != 1
            || !AuthenticationHeaderValue.TryParse(Request.Headers.Authorization[0], out var header)
            || !header.Scheme.Equals(AuthorizationScheme, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (string.IsNullOrWhiteSpace(header.Parameter)
            || !McpApiKeyComparer.Equals(header.Parameter, endpointOptions.CurrentValue.BearerToken))
        {
            LogInvalidApiKey(logger, Request.Path);
            return Task.FromResult(AuthenticateResult.Fail("A valid API key is required."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "yahoo-mail-owner")],
            SchemeName);
        return Task.FromResult(
            AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    [LoggerMessage(EventId = 3200, Level = LogLevel.Warning, Message = "Invalid API key credential on {Path}.")]
    private static partial void LogInvalidApiKey(ILogger logger, PathString path);
}

public static class McpApiKeyComparer
{
    public static bool Equals(string suppliedKey, string? configuredKey)
    {
        if (configuredKey is null)
        {
            return false;
        }

        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedKey));
        var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));
        return CryptographicOperations.FixedTimeEquals(suppliedHash, configuredHash);
    }
}