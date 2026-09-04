# Yahoo Mail MCP

Yahoo Mail MCP is a single-user .NET 10 MCP server for safely reading and organizing one Yahoo mailbox through standards-based IMAP. It supports local stdio and authenticated stateless Streamable HTTP transports.

> [!IMPORTANT]
> The service never deletes, trashes, purges, expunges, sends, drafts, replies to, forwards, or downloads attachment content. Moving is allowed only to a validated non-trash folder when the authenticated server advertises native IMAP `MOVE`.

## Features

- List folders and bounded pages of recent messages.
- Structured IMAP search without Gmail-only syntax.
- Read bounded normalized text and safe metadata.
- Mark read/unread and set/clear flagged state.
- Move to approved folders only with native `MOVE`.
- Nine identical tools over stdio and Streamable HTTP, with Entra OAuth for ChatGPT and the existing static API token for compatible clients.
- Aspire orchestration, privacy-safe telemetry, Docker, Terraform, OIDC workflows, and optional CloudFront/WAF edge protection.

See [Tool catalog and agent setup](docs/tools-and-agent-setup.md) for every tool and client configuration.

## Prerequisites

- .NET SDK 10.0.400 or a compatible .NET 10 feature band.
- PowerShell 7.
- Docker Desktop for the local IMAP profile and container tests.
- A Yahoo app password for real mailbox access. Follow Yahoo's current [third-party app password instructions](https://help.yahoo.com/kb/generate-manage-third-party-passwords-sln15241.html).
- For deployment: Terraform 1.14+, Azure CLI, AWS CLI, an Azure subscription, and an existing Route 53 public hosted zone.

## Quick start without Yahoo

Start the deterministic GreenMail profile. It uses fake local credentials and randomized ports:

```powershell
dotnet restore --locked-mode
dotnet run --project ./src/YahooMailMcp.AppHost -- --YahooMail:Profile=local-imap
```

Open the secure Aspire Dashboard URL printed by the AppHost. Select `yahoo-mail-mcp-http`, then use its HTTP endpoint with `/mcp` and this local-only token:

```text
local-test-mcp-bearer-token-32-bytes
```

The Dashboard shows resource health, structured logs, sanitized MCP diagnostics, traces, and metrics. It never shows raw mail or MCP payload values.

## Quick start with Yahoo

Create four AppHost user-secret parameters:

```powershell
dotnet user-secrets set "Parameters:yahoo-email" "your-address@yahoo.com" --project ./src/YahooMailMcp.AppHost
dotnet user-secrets set "Parameters:yahoo-app-password" "your-yahoo-app-password" --project ./src/YahooMailMcp.AppHost
dotnet user-secrets set "Parameters:cursor-signing-key" "a-random-value-of-at-least-32-bytes" --project ./src/YahooMailMcp.AppHost
dotnet user-secrets set "Parameters:mcp-bearer-token" "a-separate-random-value-of-at-least-32-bytes" --project ./src/YahooMailMcp.AppHost

dotnet run --project ./src/YahooMailMcp.AppHost
```

Use the HTTP endpoint shown by Aspire and send:

```text
Authorization: ApiKey <mcp-bearer-token>
```

Health endpoints are `/health/live` and `/health/ready`; neither repeatedly authenticates to Yahoo.

## Stdio

Build once and configure the agent to start the compiled DLL rather than `dotnet run`, which can write build output to protocol stdout:

```powershell
dotnet build ./src/YahooMailMcp.Server.Stdio --configuration Release
```

Supply `Yahoo__Email`, `Yahoo__AppPassword`, and `Cursor__SigningKey` through the client process environment. See [Tool catalog and agent setup](docs/tools-and-agent-setup.md).

## Docker

```powershell
Copy-Item .env.example .env
# Edit .env locally; it is gitignored.
docker compose config
docker compose up --build
Invoke-WebRequest http://localhost:8080/health/live
docker compose down
```

The final image is digest-pinned, non-root, capability-dropped, read-only, and uses Microsoft's ICU-capable chiseled-extra runtime plus an application-native health command because the image contains no shell or curl.

## Build and test

```powershell
dotnet restore --locked-mode
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
dotnet format --verify-no-changes --no-restore
./scripts/Check-NoDelete.ps1
```

Docker-backed suites are opt-in locally and enabled in CI:

```powershell
$env:RUN_IMAP_INTEGRATION_TESTS = 'true'
$env:RUN_ASPIRE_CLOSED_BOX_TESTS = 'true'
dotnet test --configuration Release
```

The live Yahoo smoke test is manual, read-only, and never runs in CI:

```powershell
$env:YAHOO__EMAIL = 'your-address@yahoo.com'
$env:YAHOO__APPPASSWORD = 'your-app-password'
./scripts/Invoke-LiveSmokeTest.ps1
```

## Deployment overview

Production deployment uses an operator-provided encrypted S3 bucket with native S3 lock files and two separate state keys:

1. `yahoo-mail-mcp/prod/foundation.tfstate`: ACR, Container Apps environment, Key Vault, identity/RBAC, Log Analytics, Application Insights, and alert routing.
2. `yahoo-mail-mcp/prod/application.tfstate`: immutable Container App image, versionless Key Vault references, single replica, Route 53, custom domain, managed certificate, optional CloudFront/WAF edge resources, and alerts.

After foundation, provision the four mailbox/runtime secret values outside Terraform with `scripts/Set-KeyVaultSecrets.ps1`, then create the Entra MCP app with `scripts/New-EntraMcpApplication.ps1`. Plan/apply the application stack using an immutable `sha256:` image digest. Full instructions, including ChatGPT OAuth setup, are in [Deployment](docs/deployment.md) and [Tool catalog and agent setup](docs/tools-and-agent-setup.md).

For the edge rollout, origin-bypass controls, exact WAF allowlist, secret rotation, pricing-plan caveat, and rollback sequence, see [CloudFront and AWS WAF](docs/cloudfront-waf.md). Cloud deployment remains an explicit reviewed action; this repository does not apply it automatically during local validation.

## Operations and security

- [Architecture](docs/architecture.md)
- [Authentication](docs/authentication.md)
- [CloudFront and AWS WAF](docs/cloudfront-waf.md)
- [Configuration](docs/configuration.md)
- [Deployment](docs/deployment.md)
- [Operations](docs/operations.md)
- [Security](docs/security.md)
- [Threat model](docs/threat-model.md)
- [V1 acceptance status](docs/acceptance-report.md)

Report security issues privately to the repository owner or organization security contact. Do not include credentials, tokens, message content, addresses, subjects, or attachment data in an issue or log excerpt.
