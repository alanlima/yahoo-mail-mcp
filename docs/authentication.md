# Authentication

V1 authenticates to Yahoo IMAP with an email address and Yahoo-generated app password. These values are configuration secrets and are never accepted as operation parameters, returned to callers, or included in logs.

For local Aspire runs, secrets are stored through the AppHost's .NET user-secrets parameters. Production will inject versionless Azure Key Vault references into Container Apps environment variables.

Remote Streamable HTTP supports two independent authentication methods:

- The existing static API key from the legacy `Mcp__BearerToken` configuration setting. It must contain at least 32 UTF-8 bytes and is compared without a token-dependent early exit.
- Microsoft Entra OAuth 2.0 access tokens issued for this MCP resource. OAuth is enabled with `Mcp__OAuth__Enabled=true` and requires the configured delegated scope.

Every request under the configured MCP path requires one of these methods and the configured rate-limit policy. Missing or invalid credentials receive HTTP 401; a valid OAuth token without `access_as_user` receives HTTP 403.

Use the token only in the authorization header:

```text
Authorization: ApiKey <Mcp__BearerToken value>
```

`Bearer` is reserved exclusively for OAuth JWT access tokens. Existing static-key clients must migrate from `Authorization: Bearer <key>` to `Authorization: ApiKey <key>`; the configured key and its Key Vault secret do not change.

When OAuth is enabled, the endpoint publishes RFC 9728 protected-resource metadata at `/.well-known/oauth-protected-resource`. Its 401 challenge directs OAuth-capable MCP clients to that metadata. The server validates token signature, Entra tenant issuer, application GUID audience, expiry, and the delegated scope. It never accepts an ID token as an API access token and never falls back from a rejected JWT to API-key authentication.

The Entra app's client secret is used by ChatGPT during the authorization-code exchange. The MCP runtime does not need or load that secret; it reads only the client ID needed to validate the resource configuration. The secret is stored in Key Vault for secure retrieval when configuring ChatGPT.

Both methods initialize the same endpoint. Static API key:

```bash
curl 'https://mcp.example.com/mcp' \
  -H 'Authorization: ApiKey <api-key>' \
  -H 'Accept: application/json, text/event-stream' \
  -H 'Content-Type: application/json' \
  --data '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"curl","version":"1.0"}}}'
```

Entra OAuth access token:

```bash
curl 'https://mcp.example.com/mcp' \
  -H 'Authorization: Bearer <entra-access-token>' \
  -H 'Accept: application/json, text/event-stream' \
  -H 'Content-Type: application/json' \
  --data '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"curl","version":"1.0"}}}'
```

Neither remote authentication method replaces Yahoo authentication, and Yahoo credentials never authorize the MCP endpoint. Stdio relies on the local process boundary and does not use either remote method. The `IImapAuthenticator` seam keeps any future Yahoo OAuth SASL work separate from this Entra-protected MCP boundary.
