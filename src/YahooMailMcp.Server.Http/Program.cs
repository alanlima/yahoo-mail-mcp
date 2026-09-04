using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

using ModelContextProtocol.AspNetCore;

using YahooMailMcp.Infrastructure.MailKit;
using YahooMailMcp.Server.Http;

if (args is ["--health-check", var healthUri])
{
    using var healthClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    try
    {
        using var response = await healthClient.GetAsync(healthUri).ConfigureAwait(false);
        Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (HttpRequestException)
    {
        Environment.ExitCode = 1;
    }
    catch (TaskCanceledException)
    {
        Environment.ExitCode = 1;
    }

    return;
}


var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;

    // Trust only your ingress proxy/network here.
    // Modern ASP.NET Core ignores forwarded headers from unknown proxies.
});

builder.AddServiceDefaults();
builder.AddYahooMailInfrastructure();
builder.AddYahooMailHttpTransport();

var app = builder.Build();

app.Logger.LogSafetyCapabilities();
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseMiddleware<OriginProtectionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<SanitizedMcpDiagnosticsMiddleware>();

app.MapGet("/", () => Results.Ok(new { service = "yahoo-mail-mcp", status = "scaffold" }))
    .AllowAnonymous();
app.MapHealthChecks("/health/live", new()
{
    Predicate = registration => registration.Tags.Contains("live")
})
    .AllowAnonymous();

app.MapHealthChecks("/health/ready")
    .AllowAnonymous();

app.MapDefaultEndpoints();
var mcpOptions = app.Services.GetRequiredService<IOptions<McpEndpointOptions>>().Value;
app.MapMcp(mcpOptions.Path)
    .RequireAuthorization(McpAuthenticationSchemes.AuthorizationPolicy)
    .RequireRateLimiting(HttpHostBuilderExtensions.RateLimitPolicyName);

app.Run();

public partial class Program;