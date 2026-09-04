using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

const string LocalProfile = "local-imap";
var profile = builder.Configuration["YahooMail:Profile"] ?? "yahoo";
var yahooEmail = builder.AddParameter("yahoo-email", secret: true);
var yahooAppPassword = builder.AddParameter("yahoo-app-password", secret: true);
var cursorSigningKey = builder.AddParameter("cursor-signing-key", secret: true);
var mcpBearerToken = builder.AddParameter("mcp-bearer-token", secret: true);

var http = builder.AddProject<Projects.YahooMailMcp_Server_Http>("yahoo-mail-mcp-http")
    .WithHttpHealthCheck("/health/ready")
    .WithEnvironment("YahooMail__Profile", profile);

if (profile.Equals(LocalProfile, StringComparison.OrdinalIgnoreCase))
{
    var imap = builder
        .AddContainer(
            "imap-test",
            "greenmail/standalone",
            "latest@sha256:3df66b7edd01c8a301343ca5e3601d8674760d4708655573560c24745e624fb2")
        .WithEnvironment(
            "GREENMAIL_OPTS",
            "-Dgreenmail.setup.test.smtp -Dgreenmail.setup.test.imap -Dgreenmail.hostname=0.0.0.0 "
            + "-Dgreenmail.users=test-user:deterministic-test-password@example.test -Dgreenmail.users.login=email")
        .WithEndpoint(targetPort: 3143, name: "imap", scheme: "tcp")
        .WithEndpoint(targetPort: 3025, name: "smtp", scheme: "tcp");

    http
        .WaitFor(imap)
        .WithEnvironment("Yahoo__Host", imap.GetEndpoint("imap").Property(EndpointProperty.Host))
        .WithEnvironment("Yahoo__Port", imap.GetEndpoint("imap").Property(EndpointProperty.Port))
        .WithEnvironment("Yahoo__UseSsl", "false")
        .WithEnvironment("Yahoo__AllowNonYahooEndpoint", "true")
        .WithEnvironment("Yahoo__Email", "test-user@example.test")
        .WithEnvironment("Yahoo__AppPassword", "deterministic-test-password")
        .WithEnvironment("Cursor__SigningKey", "local-test-cursor-signing-key-32-bytes")
        .WithEnvironment("Mcp__BearerToken", "local-test-mcp-bearer-token-32-bytes");
}
else
{
    http
        .WithEnvironment("Yahoo__Email", yahooEmail)
        .WithEnvironment("Yahoo__AppPassword", yahooAppPassword)
        .WithEnvironment("Cursor__SigningKey", cursorSigningKey)
        .WithEnvironment("Mcp__BearerToken", mcpBearerToken);
}

builder.Build().Run();