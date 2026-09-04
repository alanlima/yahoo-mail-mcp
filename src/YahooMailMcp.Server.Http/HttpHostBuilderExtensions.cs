using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.AspNetCore.Authentication;

using YahooMailMcp.Mcp;

namespace YahooMailMcp.Server.Http;

public static class HttpHostBuilderExtensions
{
    public const string RateLimitPolicyName = "mcp-fixed-window";

    extension(IHostApplicationBuilder builder)
    {
        public IHostApplicationBuilder AddYahooMailHttpTransport()
        {
            builder.Services.AddYahooMailMcpTelemetry();
            builder.Services.AddSingleton<HttpTransportTelemetry>();
            builder.Services.AddOptions<McpDiagnosticsOptions>()
                .BindConfiguration(McpDiagnosticsOptions.SectionName)
                .ValidateOnStart();
            builder.Services.AddSingleton<IValidateOptions<McpDiagnosticsOptions>, McpDiagnosticsOptionsValidator>();
            builder.Services.AddOptions<McpEndpointOptions>()
                .BindConfiguration(McpEndpointOptions.SectionName)
                .ValidateOnStart();
            builder.Services.AddSingleton<IValidateOptions<McpEndpointOptions>, McpEndpointOptionsValidator>();
            builder.Services.AddOptions<McpOAuthOptions>()
                .BindConfiguration(McpOAuthOptions.SectionName)
                .ValidateOnStart();
            builder.Services.AddSingleton<IValidateOptions<McpOAuthOptions>, McpOAuthOptionsValidator>();
            builder.Services.AddOptions<OriginProtectionOptions>()
                .BindConfiguration(OriginProtectionOptions.SectionName)
                .ValidateOnStart();
            builder.Services.AddSingleton<IValidateOptions<OriginProtectionOptions>, OriginProtectionOptionsValidator>();
            builder.Services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureMcpJwtBearerOptions>();
            builder.Services.AddSingleton<IConfigureOptions<McpAuthenticationOptions>, ConfigureMcpProtocolAuthenticationOptions>();
            builder.Services.AddSingleton<IAuthorizationHandler, McpDualAuthenticationAuthorizationHandler>();

            builder.Services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = McpAuthenticationSchemes.Combined;
                    options.DefaultChallengeScheme = McpAuthenticationSchemes.Challenge;
                })
                .AddPolicyScheme(McpAuthenticationSchemes.Combined, null, options =>
                    options.ForwardDefaultSelector = McpAuthenticationSchemes.SelectAuthenticateScheme)
                .AddPolicyScheme(McpAuthenticationSchemes.Challenge, null, options =>
                    options.ForwardDefaultSelector = McpAuthenticationSchemes.SelectChallengeScheme)
                .AddScheme<AuthenticationSchemeOptions, McpApiKeyAuthenticationHandler>(
                    McpApiKeyAuthenticationHandler.SchemeName,
                    _ => { })
                .AddJwtBearer(McpAuthenticationSchemes.EntraJwt, _ => { })
                .AddMcp(McpAuthenticationSchemes.OAuthChallenge, "MCP OAuth", _ => { });
            builder.Services.AddAuthorization(options =>
            {
                options.AddPolicy(McpAuthenticationSchemes.AuthorizationPolicy, policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.AddRequirements(new McpDualAuthenticationRequirement());
                });
            });
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = (context, _) =>
                {
                    context.HttpContext.RequestServices
                        .GetRequiredService<HttpTransportTelemetry>()
                        .RecordRateLimitRejection();
                    return ValueTask.CompletedTask;
                };
                options.AddPolicy(RateLimitPolicyName, context =>
                {
                    var endpointOptions = context.RequestServices
                        .GetRequiredService<IOptions<McpEndpointOptions>>()
                        .Value;
                    return RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: "single-yahoo-owner",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = endpointOptions.RequestsPerMinute,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        });
                });
            });

            builder.Services
                .AddMcpServer()
                .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
                .WithToolsFromAssembly(typeof(YahooMailTools).Assembly);

            return builder;
        }
    }
}