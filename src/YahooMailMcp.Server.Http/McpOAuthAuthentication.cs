using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;

namespace YahooMailMcp.Server.Http;

internal sealed class McpDualAuthenticationRequirement : IAuthorizationRequirement
{
}

internal sealed class McpDualAuthenticationAuthorizationHandler(
    IOptionsMonitor<McpOAuthOptions> oauthOptions)
    : AuthorizationHandler<McpDualAuthenticationRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        McpDualAuthenticationRequirement requirement)
    {
        if (McpAuthenticationSchemes.HasRequiredAccess(
                context.User,
                oauthOptions.CurrentValue.RequiredScope))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

internal sealed class ConfigureMcpJwtBearerOptions(IOptions<McpOAuthOptions> oauthOptions)
    : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options)
    {
        if (!string.Equals(name, McpAuthenticationSchemes.EntraJwt, StringComparison.Ordinal))
        {
            return;
        }

        var oauth = oauthOptions.Value;
        if (!oauth.Enabled)
        {
            return;
        }

        options.Authority = oauth.Authority;
        options.Audience = oauth.EffectiveAudience;
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            AuthenticationType = McpAuthenticationSchemes.EntraJwt,
            ValidateAudience = true,
            ValidateIssuer = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
            NameClaimType = "name"
        };
    }

    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);
}

internal sealed class ConfigureMcpProtocolAuthenticationOptions(IOptions<McpOAuthOptions> oauthOptions)
    : IConfigureNamedOptions<McpAuthenticationOptions>
{
    public void Configure(string? name, McpAuthenticationOptions options)
    {
        if (!string.Equals(name, McpAuthenticationSchemes.OAuthChallenge, StringComparison.Ordinal))
        {
            return;
        }

        var oauth = oauthOptions.Value;
        if (!oauth.Enabled)
        {
            return;
        }

        var resource = new Uri(oauth.Resource!, UriKind.Absolute);
        options.ResourceMetadataUri = new Uri(resource, "/.well-known/oauth-protected-resource");
        options.ResourceMetadata = new ProtectedResourceMetadata
        {
            Resource = oauth.Resource,
            ResourceName = "Yahoo Mail MCP",
            AuthorizationServers = [oauth.Authority],
            BearerMethodsSupported = ["header"],
            ScopesSupported = [oauth.QualifiedScope, "offline_access", "openid", "profile"]
        };
    }

    public void Configure(McpAuthenticationOptions options) => Configure(Options.DefaultName, options);
}

internal static class McpAuthenticationSchemes
{
    public const string Combined = "McpCombined";
    public const string Challenge = "McpChallenge";
    public const string EntraJwt = "McpEntraJwt";
    public const string OAuthChallenge = "McpOAuthChallenge";
    public const string AuthorizationPolicy = "McpApiTokenOrOAuth";

    public static string SelectAuthenticateScheme(HttpContext context)
    {
        if (context.Request.Headers.Authorization.Count != 1
            || !System.Net.Http.Headers.AuthenticationHeaderValue.TryParse(
                context.Request.Headers.Authorization[0],
                out var authorization))
        {
            return McpApiKeyAuthenticationHandler.SchemeName;
        }

        if (authorization.Scheme.Equals("ApiKey", StringComparison.OrdinalIgnoreCase))
        {
            return McpApiKeyAuthenticationHandler.SchemeName;
        }

        var oauth = context.RequestServices.GetRequiredService<IOptionsMonitor<McpOAuthOptions>>().CurrentValue;
        return oauth.Enabled && authorization.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase)
            ? EntraJwt
            : McpApiKeyAuthenticationHandler.SchemeName;
    }

    public static string SelectChallengeScheme(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptionsMonitor<McpOAuthOptions>>().CurrentValue.Enabled
            ? OAuthChallenge
            : McpApiKeyAuthenticationHandler.SchemeName;

    public static bool HasRequiredAccess(ClaimsPrincipal user, string scope)
    {
        if (user.Identities.Any(identity =>
                identity.IsAuthenticated
                && identity.AuthenticationType == McpApiKeyAuthenticationHandler.SchemeName))
        {
            return true;
        }

        return user.Identities
            .Where(identity => identity.IsAuthenticated && identity.AuthenticationType == EntraJwt)
            .SelectMany(identity => identity.FindAll("scp"))
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);
    }
}