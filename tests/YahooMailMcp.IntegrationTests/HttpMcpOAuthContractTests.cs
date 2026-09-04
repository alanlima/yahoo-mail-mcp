using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

using ModelContextProtocol.Client;

namespace YahooMailMcp.IntegrationTests;

public sealed class HttpMcpOAuthContractTests : IClassFixture<HttpMcpOAuthContractTests.OAuthApplicationFactory>
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private readonly OAuthApplicationFactory factory;

    public HttpMcpOAuthContractTests(OAuthApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task MissingTokenReturnsOAuthProtectedResourceChallenge()
    {
        using var client = factory.CreateOAuthClient();
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/mcp", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(
            response.Headers.WwwAuthenticate,
            value => value.Parameter?.Contains(
                "https://localhost/.well-known/oauth-protected-resource",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task ProtectedResourceMetadataPointsToEntraAndRequiredScope()
    {
        using var client = factory.CreateOAuthClient();

        using var response = await client.GetAsync("/.well-known/oauth-protected-resource");
        var body = await response.Content.ReadAsStringAsync();
        using var metadata = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            OAuthApplicationFactory.Resource,
            metadata.RootElement.GetProperty("resource").GetString());
        Assert.Contains(OAuthApplicationFactory.Authority, body, StringComparison.Ordinal);
        Assert.Contains(OAuthApplicationFactory.QualifiedScope, body, StringComparison.Ordinal);
        Assert.DoesNotContain("client_secret", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EntraAccessTokenWithRequiredScopeListsTools()
    {
        using var httpClient = factory.CreateOAuthClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(httpClient.BaseAddress!, "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                EnableStandaloneGetStream = false,
                AdditionalHeaders = new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {OAuthApplicationFactory.CreateAccessToken("access_as_user")}"
                }
            },
            httpClient,
            ownsHttpClient: false);
        await using var mcpClient = await McpClient.CreateAsync(transport);

        var tools = await mcpClient.ListToolsAsync();

        Assert.Equal(9, tools.Count);
    }

    [Fact]
    public async Task EntraAccessTokenWithoutRequiredScopeIsForbidden()
    {
        using var client = factory.CreateOAuthClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            OAuthApplicationFactory.CreateAccessToken("wrong_scope"));
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/mcp", content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ExistingApiTokenStillAuthenticatesWhenOAuthIsEnabled()
    {
        using var httpClient = factory.CreateOAuthClient();
        var transport = CreateTransport(httpClient, $"ApiKey {ApiKey}");
        await using var mcpClient = await McpClient.CreateAsync(transport);

        var tools = await mcpClient.ListToolsAsync();

        Assert.Equal(9, tools.Count);
    }

    [Fact]
    public async Task InvalidApiKeyIsUnauthorized()
    {
        using var response = await PostWithAuthorizationAsync("ApiKey invalid-api-key");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiKeySentAsBearerIsNotAcceptedAsApiKey()
    {
        using var response = await PostWithAuthorizationAsync($"Bearer {ApiKey}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpointsRemainAnonymous()
    {
        using var client = factory.CreateOAuthClient();

        using var live = await client.GetAsync("/health/live");
        using var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public void DualSelectorAndOAuthChallengeRemainTheDefaultSchemes()
    {
        var options = factory.Services.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        Assert.Equal("McpCombined", options.DefaultAuthenticateScheme);
        Assert.Equal("McpChallenge", options.DefaultChallengeScheme);
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("eyJhbGciOiJub25lIn0.eyJzdWIiOiJ1c2VyIn0.")]
    public async Task MalformedBearerIsUnauthorizedWithoutApiKeyFallback(string token)
    {
        using var response = await PostWithAuthorizationAsync($"Bearer {token}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredJwtIsUnauthorized()
    {
        using var response = await PostWithAuthorizationAsync(
            $"Bearer {OAuthApplicationFactory.CreateAccessToken("access_as_user", expires: DateTime.UtcNow.AddMinutes(-5))}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("wrong-audience", null)]
    [InlineData(null, "https://issuer.example.test/v2.0")]
    public async Task JwtWithWrongAudienceOrIssuerIsUnauthorized(string? audience, string? issuer)
    {
        var token = OAuthApplicationFactory.CreateAccessToken(
            "access_as_user",
            audience: audience,
            issuer: issuer);

        using var response = await PostWithAuthorizationAsync($"Bearer {token}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task JwtWithWrongSignatureIsUnauthorized()
    {
        using var response = await PostWithAuthorizationAsync(
            $"Bearer {OAuthApplicationFactory.CreateAccessToken("access_as_user", useAlternateSigningKey: true)}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnknownAuthorizationSchemeIsUnauthorized()
    {
        using var response = await PostWithAuthorizationAsync("Basic dXNlcjpwYXNzd29yZA==");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MultipleAuthorizationCredentialsAreRejected()
    {
        using var client = factory.CreateOAuthClient();
        using var request = CreateMcpRequest();
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            [$"ApiKey {ApiKey}", $"Bearer {OAuthApplicationFactory.CreateAccessToken("access_as_user")}"]);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpResponseMessage> PostWithAuthorizationAsync(string authorization)
    {
        var client = factory.CreateOAuthClient();
        var request = CreateMcpRequest();
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        var response = await client.SendAsync(request);
        request.Dispose();
        client.Dispose();
        return response;
    }

    private static HttpRequestMessage CreateMcpRequest() => new(
        HttpMethod.Post,
        "/mcp")
    {
        Content = new StringContent(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"auth-test\",\"version\":\"1.0\"}}}",
            Encoding.UTF8,
            "application/json")
    };

    private static HttpClientTransport CreateTransport(HttpClient client, string authorization) => new(
        new HttpClientTransportOptions
        {
            Endpoint = new Uri(client.BaseAddress!, "/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            EnableStandaloneGetStream = false,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = authorization }
        },
        client,
        ownsHttpClient: false);

    public sealed class OAuthApplicationFactory : WebApplicationFactory<Program>
    {
        public const string Authority = "https://login.microsoftonline.com/11111111-1111-4111-8111-111111111111/v2.0";
        public const string Audience = "22222222-2222-4222-8222-222222222222";
        public const string Resource = "https://localhost/mcp";
        public const string QualifiedScope = "https://localhost/mcp/access_as_user";
        private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "integration-test" };
        private static readonly RsaSecurityKey AlternateSigningKey = new(RSA.Create(2048)) { KeyId = "wrong-key" };

        public HttpClient CreateOAuthClient() => CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        public static string CreateAccessToken(
            string scope,
            string? audience = null,
            string? issuer = null,
            DateTime? expires = null,
            bool useAlternateSigningKey = false)
        {
            var now = DateTime.UtcNow;
            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = issuer ?? Authority,
                Audience = audience ?? Audience,
                NotBefore = now.AddMinutes(-10),
                Expires = expires ?? now.AddMinutes(5),
                Subject = new ClaimsIdentity(
                [
                    new Claim("sub", "single-yahoo-owner"),
                    new Claim("scp", scope)
                ]),
                SigningCredentials = new SigningCredentials(
                    useAlternateSigningKey ? AlternateSigningKey : SigningKey,
                    SecurityAlgorithms.RsaSha256)
            };
            return new JsonWebTokenHandler().CreateToken(descriptor);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Yahoo:Email"] = "configured-test-address",
                    ["Yahoo:AppPassword"] = "configured-test-app-password",
                    ["Cursor:SigningKey"] = "0123456789abcdef0123456789abcdef",
                    ["Mcp:BearerToken"] = ApiKey,
                    ["Mcp:OAuth:Enabled"] = "true",
                    ["Mcp:OAuth:TenantId"] = "11111111-1111-4111-8111-111111111111",
                    ["Mcp:OAuth:ClientId"] = Audience,
                    ["Mcp:OAuth:Audience"] = Audience,
                    ["Mcp:OAuth:Resource"] = Resource,
                    ["Mcp:OAuth:RequiredScope"] = "access_as_user",
                    ["AllowedHosts"] = "localhost;127.0.0.1"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>("McpEntraJwt", options =>
                {
                    var configuration = new OpenIdConnectConfiguration { Issuer = Authority };
                    configuration.SigningKeys.Add(SigningKey);
                    options.Configuration = configuration;
                    options.TokenValidationParameters.ValidIssuer = Authority;
                    options.TokenValidationParameters.ValidAudience = Audience;
                    options.TokenValidationParameters.IssuerSigningKey = SigningKey;
                });
            });
        }
    }
}