# Architecture

The solution follows this dependency direction:

```text
Domain <- Application <- Infrastructure.MailKit
                   ^   <- Mcp <- Server.Http
                   |          <- Server.Stdio

ServiceDefaults <- Server.Http <- AppHost
```

`YahooImapSession` owns one MailKit `ImapClient` and serializes each complete operation with a semaphore because the client is not thread-safe. It connects and authenticates lazily, refreshes an immutable capability projection after authentication, reuses healthy connections, and retries transient failures only for read operations.

Public message identifiers contain the folder full name, UID, and UIDVALIDITY. The gateway never exposes IMAP sequence indexes. Pagination cursors contain only a version, folder, UIDVALIDITY, direction, last UID, and issue time; their HMAC prevents client-side alteration.

Yahoo is treated as standards-based IMAP. Optional standard features are enabled only when advertised. Gmail-specific capabilities do not enable labels, raw search, or Gmail identifiers.

The shared `YahooMailMcp.Mcp` adapter defines the transport-neutral tool surface and depends on the application gateway rather than MailKit. Both executable transports discover the same tool assembly. The HTTP host adds bearer authentication and fixed-window rate limiting; the stdio host redirects all console logging to stderr to keep stdout protocol-clean.

The gateway supports read/unread and flagged/unflagged state plus normal-folder moves. A move is permitted only after the active session advertises native IMAP `MOVE` and the destination passes special-use and configured deny-list checks. Copy-and-remove fallback behavior is prohibited.

Aspire is the local orchestration and diagnostics entry point. It is not the production deployment mechanism; reviewed Terraform is the production source of truth. Foundation and application have distinct encrypted state objects in an operator-provided S3 bucket, while Azure runtime resources use managed identity and Route 53 owns public DNS.

Production can add this edge boundary without moving the application out of Azure:

```text
Internet -> Route 53 -> CloudFront -> AWS WAF -> HTTPS -> Azure Container Apps -> YahooMailMcp
```

CloudFront forwards the authentication and MCP protocol headers without caching responses. WAF is default-deny and admits only the repository's exact public method/path contract after managed and rate-limit checks. CloudFront adds a private origin-verification header; application middleware validates and removes it before the existing API-key or Entra authentication runs. Azure health probes bypass only that middleware on `/health/live` and `/health/ready`. See [CloudFront and AWS WAF](cloudfront-waf.md).
