# Configuration

Non-secret defaults are stored in the HTTP server's `appsettings.json`. Environment variables use double underscores, such as `Yahoo__Host`.

Required secrets:

- `Yahoo__Email`
- `Yahoo__AppPassword`
- `Cursor__SigningKey`, containing at least 32 UTF-8 bytes
- `Mcp__BearerToken`, the legacy configuration name for the static API key, containing at least 32 UTF-8 bytes

Production OAuth settings:

- `Mcp__OAuth__Enabled=true`
- `Mcp__OAuth__TenantId`: Entra tenant GUID
- `Mcp__OAuth__ClientId`: app/client GUID, injected from Key Vault
- `Mcp__OAuth__Audience`: application/client GUID expected in the JWT `aud` claim
- `Mcp__OAuth__Resource`: canonical protected resource, normally `https://<host>/mcp`
- `Mcp__OAuth__RequiredScope=access_as_user`

The base default is false, while `appsettings.Development.json` intentionally enables OAuth with the non-secret Entra tenant/client/audience/resource settings so the same dual-auth behavior can be debugged locally. Local Aspire clients may still use the static API key. The Entra client secret is not a server runtime setting.

The Aspire AppHost maps these from four secret parameters: `yahoo-email`, `yahoo-app-password`, `cursor-signing-key`, and `mcp-bearer-token`. Store them with `dotnet user-secrets` on the AppHost project; do not place values in AppHost settings or launch profiles.

Production validation requires `imap.mail.yahoo.com`, port `993`, and SSL. Local deterministic IMAP tests must explicitly set `Yahoo__AllowNonYahooEndpoint=true` before a non-Yahoo endpoint is accepted.

Read limits default to 10 messages per page, a maximum page size of 50, and 50,000 body characters. The product hard maximum is 200,000 characters. Search requires a folder and at least one structured filter.

`.env.example` contains placeholders only. `.env` variants other than that example, user secrets, Terraform state, and local plan files are ignored.

Infrastructure options use `BindConfiguration` through the host builder. Registration does not receive or capture an `IConfiguration` instance, so configuration providers composed during host setup—including Azure App Configuration and Key Vault-backed providers—remain visible when options are resolved and validated at startup.

HTTP transport options are registered the same way from the `Mcp` section. `Mcp__Path` defaults to `/mcp`, and `Mcp__RequestsPerMinute` defaults to `60`.

CloudFront origin protection uses:

- `OriginProtection__Enabled=false` by default and in local development.
- `OriginProtection__HeaderName=X-Origin-Verify`.
- `OriginProtection__HeaderValue`, loaded from the `origin-verification-header` Key Vault secret in production.

Enabled values must contain at least 32 UTF-8 bytes. The middleware never logs them and removes the verification header before downstream request handling.

## Aspire MCP diagnostics

The Development profile enables `McpDiagnostics__EnableSanitizedPayloadLogging`. In the Aspire Dashboard, this adds:

- MCP method and approved tool name.
- Argument field names, but never argument values.
- Request size, HTTP status, outcome, safe error code, and duration.
- Tool-call traces and the `yahoo_mail_mcp_tool_calls_total`, `yahoo_mail_mcp_tool_duration_ms`, and `yahoo_mail_mcp_active_requests` metrics.

Full request and response payloads remain unavailable because they can contain credentials, search text, folder names, addresses, subjects, and message bodies. The diagnostics option fails startup validation if enabled outside the `Development` environment.

To disable the additional local metadata logs:

```text
McpDiagnostics__EnableSanitizedPayloadLogging=false
```
