# Yahoo Mail MCP — End-to-End Implementation Specification

**Status:** Implementation-ready specification  
**Revision:** 2026-08-30 — Aspire orchestration, CAF naming, and Yahoo capability compatibility added  
**Target project folder:** `D:\source\YahooMailMcp`  
**Primary runtime:** .NET 10 LTS / C# 14  
**Local orchestration:** Aspire AppHost, Service Defaults, Dashboard, and Aspire integration tests  
**Primary deployment:** Azure Container Apps  
**Infrastructure:** Terraform, Azure Container Registry, Azure Key Vault, Azure Monitor, AWS Route 53  
**Authentication:** Yahoo app password in V1; OAuth 2.0 + IMAP SASL XOAUTH2/OAUTHBEARER-ready abstraction for a later release  
**Core invariant:** The service must never expose or execute a user-visible email deletion operation.

---

## 1. Purpose

Build a personal, single-user Model Context Protocol (MCP) server that allows an authorized MCP client to read and organize one Yahoo Mail account through IMAP.

The first production-capable release must:

- Connect to Yahoo IMAP using an email address and Yahoo app password.
- List folders and recent messages.
- Search messages.
- Read message metadata and bounded text content.
- Mark messages read or unread.
- Set or clear the flagged state.
- Move messages only to validated, non-trash destination folders and only when Yahoo advertises native IMAP `MOVE`.
- Run locally over stdio and remotely over MCP Streamable HTTP.
- Run the complete local topology through Aspire so health, logs, traces, metrics, resource state, endpoints, and integration tests are available from one developer experience.
- Require authentication on the remote MCP endpoint.
- Package as a non-root Docker image.
- Deploy through Terraform and GitHub Actions using OIDC, with no long-lived Azure or AWS cloud credentials in GitHub.
- Store Yahoo and remote MCP secrets in Azure Key Vault.
- Emit privacy-safe logs, metrics, and traces to Azure Monitor.

The service must not:

- Delete, trash, purge, or expunge messages through any MCP tool.
- Expose raw IMAP commands.
- Send, draft, forward, reply to, or otherwise transmit email.
- Download attachments in V1.
- Permanently persist message bodies, headers, mailbox indexes, or search results.
- Log message bodies, subjects, addresses, credentials, access tokens, app passwords, or remote MCP API keys.
- Support multiple Yahoo users in V1.

---

## 2. Non-Negotiable Safety Invariant: No Delete

The no-delete requirement is an architectural control, not merely a tool-description warning.

### 2.1 Forbidden capabilities

No production code may call or expose:

- `IMailFolder.Expunge*`
- `IMailFolder.Delete*`
- `IMailFolder.AddFlags*` or `SetFlags*` with `MessageFlags.Deleted`
- Any raw IMAP `STORE ... \\Deleted`, `EXPUNGE`, `UID EXPUNGE`, or equivalent command
- Any tool named or described as delete, remove, purge, empty, trash, clear, cleanup, or expunge
- Any move whose destination resolves to a folder with the IMAP special-use attribute `\\Trash`
- Any move whose destination canonical name matches a configured trash deny-list

Do not add a hidden or administrative deletion path.

### 2.2 Permitted move semantics

Moving a message to a normal folder is allowed only when the authenticated Yahoo server advertises native IMAP `MOVE`. MailKit can otherwise emulate a move by copying and removing the source; that fallback is forbidden because it relies on deletion semantics. Application code must validate both the destination and the active capability set before invoking MailKit's high-level move operation. Application code must never directly set `MessageFlags.Deleted` or call `Expunge`.

This distinction must be documented in code-level architecture tests: a validated native move is a supported organization operation; emulated move, permanent deletion, and moving to trash are not.

### 2.3 Defense in depth

Implement all of the following:

1. No deletion method on the application port or MailKit gateway interface.
2. No deletion MCP tool.
3. A destination-folder policy that checks special-use attributes and canonical deny-list names.
4. Reflection/architecture tests that fail if forbidden tool names or forbidden MailKit method names appear in production assemblies.
5. Source scan in CI for `MessageFlags.Deleted`, `Expunge`, and direct raw IMAP command construction, with a small explicit allow-list for test files and this specification.
6. A startup log event stating `mail.delete_capability=false`.
7. Acceptance tests proving trash destinations are rejected before any mutation occurs.
8. Native `MOVE` capability validation. If Yahoo does not advertise IMAP `MOVE`, reject the move as unsupported; do not let MailKit fall back to copy plus `\\Deleted`/expunge semantics.

---

## 3. Scope and Release Strategy

### V1: App-password release

V1 is a single-account personal service. Yahoo credentials are supplied through configuration and production secrets come from Key Vault references injected into the container as environment variables.

V1 does not implement a browser login flow. The user creates a Yahoo app password outside this service and stores it in Key Vault. App passwords must never be accepted as MCP tool parameters.

### V1.1: Operational hardening

- Retry and connection recovery tuning.
- Dashboards, alerts, and runbooks.
- Security review.
- Optional IP restrictions at Azure Container Apps ingress.
- Secret rotation validation.

### V2: Yahoo OAuth

Add Yahoo OAuth only after restricted Yahoo Mail API access has been approved. Preserve the application and IMAP gateway contracts. Introduce authorization-code flow with PKCE where Yahoo supports the required combination, encrypted refresh-token storage, proactive token refresh, and MailKit SASL OAuth authentication.

The future implementation should authenticate MailKit with a SASL mechanism supported by Yahoo and the approved scope, typically `SaslMechanismOAuth2(email, accessToken)` for XOAUTH2. If Yahoo explicitly supports standard OAUTHBEARER for the approved application, provide it as a provider capability rather than hard-coding an assumption.

Yahoo restricted mail scopes require developer approval. Do not claim OAuth mail support is complete until a real approved account passes a live IMAP smoke test.

---

## 4. Architecture

Use a small clean-architecture split so Yahoo authentication and MCP transports can change independently.

```text
Aspire AppHost (local development and closed-box tests)
  |-- Dashboard: resources, health, logs, traces, metrics
  |-- YahooMailMcp.Server.Http
  `-- Local IMAP test container (test profile only)

MCP client
  |-- stdio --------------------------> YahooMailMcp.Server.Stdio
  |-- HTTPS Streamable HTTP ----------> YahooMailMcp.Server.Http
                                             |
                                             v
                                  YahooMailMcp.Application
                                    tools + policies + DTOs
                                             |
                                             v
                                     IYahooMailGateway
                                             |
                                             v
                               YahooMailMcp.Infrastructure.MailKit
                                             |
                                             v
                                  imap.mail.yahoo.com:993/TLS
```

### 4.1 Project responsibilities

`YahooMailMcp.Domain`

- Stable mail identifiers and value objects.
- Message/folder models.
- Domain exceptions and error codes.
- No references to MailKit, MCP, ASP.NET Core, Azure, or configuration packages.

`YahooMailMcp.Application`

- Use cases and interfaces.
- Validation, limits, cursor handling, and trash-folder policy.
- Tool result DTOs with explicit JSON names.
- No transport-specific response objects.

`YahooMailMcp.Infrastructure.MailKit`

- TLS connection to Yahoo.
- MailKit folder/message mapping.
- V1 app-password authentication.
- Future OAuth authenticator seam.
- Connection lifecycle, serialization, reconnect logic, and IMAP capability checks.

`YahooMailMcp.Server.Http`

- ASP.NET Core host.
- Stateless MCP Streamable HTTP transport.
- Remote bearer-token middleware.
- Health endpoints, rate limiting, OpenTelemetry, and host filtering.
- Container entry point.

`YahooMailMcp.Server.Stdio`

- Local MCP stdio entry point.
- Logs only to stderr; stdout is reserved for MCP protocol messages.
- Uses the same tools, policies, and MailKit infrastructure.

`YahooMailMcp.AppHost`

- Is the required default local-development entry point.
- Orchestrates the HTTP MCP server and the local IMAP test container/profile.
- Supplies development-only configuration through Aspire parameters and references.
- Exposes the Aspire Dashboard for resource status, structured logs, traces, and metrics.
- Does not provision Azure resources and is not the production deployment mechanism.

`YahooMailMcp.ServiceDefaults`

- Centralizes health checks, service discovery, resilience defaults that are relevant to HTTP, and OpenTelemetry configuration.
- Is referenced by the HTTP server and any future executable service.
- Must preserve this specification's telemetry redaction rules; Aspire defaults are a baseline, not permission to capture MCP or mail payloads.

### 4.2 Dependency direction

```text
Domain <- Application <- Infrastructure.MailKit
                       <- Server.Http
                       <- Server.Stdio

ServiceDefaults <- Server.Http
Server.Http <- AppHost
AppHost <- AspireTests
```

Server projects may reference Application and Infrastructure for dependency registration. Domain and Application must not reference server or infrastructure projects.

### 4.3 Connection model

MailKit clients are not thread-safe. Register a singleton `YahooImapSession` with a `SemaphoreSlim(1, 1)` around each complete IMAP operation.

The session must:

- Connect to `imap.mail.yahoo.com` on port `993` using `SecureSocketOptions.SslOnConnect`.
- Enforce certificate validation; never set a permissive certificate callback.
- Authenticate lazily on the first operation.
- Reuse the connection while healthy.
- Send `NOOP` or reconnect after a configurable idle threshold.
- Disconnect cleanly during host shutdown.
- Retry one time for transient connection failures on read-only operations.
- Not automatically retry state-changing operations after an ambiguous network failure.
- Translate provider errors into stable application error codes.
- Respect cancellation tokens on every asynchronous MailKit call.

The HTTP MCP server should run one active Container Apps replica in V1 because the service has one account and a serialized IMAP session. Configure `min_replicas = 1` and `max_replicas = 1`. Revisit this only after adding distributed account/session coordination. Aspire is used for local orchestration, diagnostics, and tests; Terraform remains the source of truth for Azure production infrastructure.

---

## 5. Repository Tree

The coding agent must create this structure in the blank project folder:

```text
YahooMailMcp/
├─ .config/
│  └─ dotnet-tools.json
├─ .github/
│  ├─ CODEOWNERS
│  ├─ dependabot.yml
│  └─ workflows/
│     ├─ ci.yml
│     ├─ infrastructure.yml
│     └─ release.yml
├─ docs/
│  ├─ architecture.md
│  ├─ authentication.md
│  ├─ configuration.md
│  ├─ deployment.md
│  ├─ operations.md
│  ├─ security.md
│  └─ threat-model.md
├─ infra/
│  └─ terraform/
│     ├─ bootstrap/
│     │  ├─ main.tf
│     │  ├─ outputs.tf
│     │  ├─ providers.tf
│     │  ├─ variables.tf
│     │  └─ versions.tf
│     ├─ foundation/
│     │  ├─ main.tf
│     │  ├─ monitoring.tf
│     │  ├─ outputs.tf
│     │  ├─ providers.tf
│     │  ├─ variables.tf
│     │  └─ versions.tf
│     ├─ application/
│     │  ├─ container-app.tf
│     │  ├─ dns.tf
│     │  ├─ identity.tf
│     │  ├─ main.tf
│     │  ├─ outputs.tf
│     │  ├─ providers.tf
│     │  ├─ variables.tf
│     │  └─ versions.tf
│     └─ environments/
│        └─ prod/
│           ├─ application.tfvars.example
│           ├─ backend.hcl.example
│           └─ foundation.tfvars.example
├─ scripts/
│  ├─ Check-NoDelete.ps1
│  ├─ Invoke-LiveSmokeTest.ps1
│  ├─ Set-KeyVaultSecrets.ps1
│  └─ Verify-Local.ps1
├─ src/
│  ├─ YahooMailMcp.AppHost/
│  │  ├─ AppHost.cs
│  │  ├─ appsettings.json
│  │  ├─ Properties/
│  │  │  └─ launchSettings.json
│  │  └─ YahooMailMcp.AppHost.csproj
│  ├─ YahooMailMcp.Domain/
│  │  └─ YahooMailMcp.Domain.csproj
│  ├─ YahooMailMcp.Application/
│  │  └─ YahooMailMcp.Application.csproj
│  ├─ YahooMailMcp.Infrastructure.MailKit/
│  │  └─ YahooMailMcp.Infrastructure.MailKit.csproj
│  ├─ YahooMailMcp.Server.Http/
│  │  ├─ Dockerfile
│  │  ├─ Program.cs
│  │  └─ YahooMailMcp.Server.Http.csproj
│  ├─ YahooMailMcp.Server.Stdio/
│  │  ├─ Program.cs
│  │  └─ YahooMailMcp.Server.Stdio.csproj
│  └─ YahooMailMcp.ServiceDefaults/
│     ├─ Extensions.cs
│     └─ YahooMailMcp.ServiceDefaults.csproj
├─ tests/
│  ├─ YahooMailMcp.AspireTests/
│  │  └─ YahooMailMcp.AspireTests.csproj
│  ├─ YahooMailMcp.ArchitectureTests/
│  │  └─ YahooMailMcp.ArchitectureTests.csproj
│  ├─ YahooMailMcp.IntegrationTests/
│  │  └─ YahooMailMcp.IntegrationTests.csproj
│  └─ YahooMailMcp.UnitTests/
│     └─ YahooMailMcp.UnitTests.csproj
├─ .dockerignore
├─ .editorconfig
├─ .env.example
├─ .gitattributes
├─ .gitignore
├─ AGENTS.md
├─ Directory.Build.props
├─ Directory.Packages.props
├─ docker-compose.yml
├─ global.json
├─ IMPLEMENTATION_SPEC.md
├─ LICENSE
├─ README.md
└─ YahooMailMcp.slnx
```

If `.slnx` tooling is unavailable in the installed SDK, use `YahooMailMcp.sln`; do not block the project on solution format.

---

## 6. Required `AGENTS.md`

Create `AGENTS.md` at repository root with the following content, adjusting only commands that differ after scaffolding:

```markdown
# AGENTS.md

## Mission

Build and maintain a secure, single-user Yahoo Mail MCP server. The service reads and organizes mail but never deletes, trashes, purges, expunges, sends, drafts, replies to, or forwards messages.

## Non-Negotiable Rules

- Never add an MCP deletion, trash, purge, cleanup, or expunge tool.
- Never call MailKit delete/expunge APIs or set `MessageFlags.Deleted`.
- Never move a message to a folder marked `\\Trash` or matching the configured trash deny-list.
- Never expose raw IMAP commands.
- Never log secrets, tokens, app passwords, message bodies, subjects, email addresses, or attachment contents.
- Never place real credentials in source, tests, Docker layers, Terraform, state, workflow files, examples, snapshots, or logs.
- Keep message bodies in memory only for the duration of a request.
- Keep stdout protocol-clean in the stdio server; write diagnostics to stderr.
- Treat live Yahoo tests as opt-in and never run them in CI.
- Use the Aspire AppHost as the default local orchestration entry point and use the Aspire Dashboard/CLI for local health and telemetry diagnosis.
- Do not use Aspire publishing as a substitute for the reviewed Terraform production deployment.
- Treat Yahoo as standards-based IMAP with runtime capability negotiation. Never use Gmail-only `X-GM-EXT1`, labels, raw Gmail search, Gmail thread IDs, or Gmail message IDs.
- Do not invoke MailKit move APIs unless the authenticated Yahoo session advertises native IMAP `MOVE`.

## Engineering Standards

- Target the SDK pinned by `global.json` and use nullable reference types.
- Use async APIs and pass `CancellationToken` through every layer.
- Keep Domain and Application independent of MailKit, MCP transports, ASP.NET Core, and Azure.
- Use centralized package versions in `Directory.Packages.props`.
- Pin GitHub Actions to full commit SHAs and include a version comment.
- Pin container base images by digest before production release.
- Keep Terraform formatted, validated, and free of secret values.
- Generate Azure resource names from centralized Terraform locals using Microsoft Cloud Adoption Framework abbreviations and the repository naming matrix.
- Prefer small, typed DTOs over returning provider objects.
- Return stable, machine-readable error codes and safe human-readable messages.
- Make the smallest focused change that satisfies the active phase.

## Required Validation

Run before declaring work complete:

```powershell
dotnet restore --locked-mode
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
dotnet format --verify-no-changes
./scripts/Check-NoDelete.ps1
dotnet test ./tests/YahooMailMcp.AspireTests/YahooMailMcp.AspireTests.csproj --configuration Release --no-build
docker build -f ./src/YahooMailMcp.Server.Http/Dockerfile -t yahoo-mail-mcp:local .
terraform -chdir=infra/terraform/foundation fmt -check -recursive
terraform -chdir=infra/terraform/foundation init -backend=false
terraform -chdir=infra/terraform/foundation validate
terraform -chdir=infra/terraform/application fmt -check -recursive
terraform -chdir=infra/terraform/application init -backend=false
terraform -chdir=infra/terraform/application validate
```

If Docker, Terraform, Azure, AWS, or live Yahoo access is unavailable, complete all other checks and report exactly what remains unverified.

## Workflow

1. Read `IMPLEMENTATION_SPEC.md` and relevant files before editing.
2. Inspect the worktree and preserve unrelated user changes.
3. Implement one phase at a time in dependency order.
4. Add or update tests with each behavior change.
5. Run the narrowest relevant checks first, then the full validation suite.
6. Update README and operational documentation when behavior or configuration changes.
7. Continue autonomously through safe local implementation and validation. Ask before destructive actions, external production changes, purchases, or material scope expansion.

## Completion Report

Summarize changed files, behavior delivered, tests run, remaining risks, and any manual cloud/Yahoo steps. Do not claim live integration or deployment succeeded without evidence.
```

---

## 7. .NET Solution Setup

### 7.1 SDK and language

- Target `net10.0`.
- Pin an installed .NET 10 SDK feature band in `global.json` with `rollForward` set to `latestFeature`.
- Enable nullable reference types, implicit usings, deterministic builds, warnings as errors in CI, analyzers, and XML documentation generation for public APIs.
- Use C# 14 only where it improves clarity; avoid novelty for its own sake.

If .NET 10 is unavailable in the execution environment, the agent may temporarily build with .NET 9 only to unblock local scaffolding, but the committed target remains .NET 10 unless the user explicitly changes it.

### 7.2 Packages

Resolve and pin current stable versions at implementation time in `Directory.Packages.props`. Generate `packages.lock.json` files and use locked restore in CI.

Required package families:

- Current stable Aspire AppHost SDK/package for `YahooMailMcp.AppHost`.
- `Microsoft.Extensions.ServiceDiscovery` and the current Aspire Service Defaults dependencies used by the template.
- `Aspire.Hosting.Testing` for closed-box distributed application tests.
- `ModelContextProtocol.AspNetCore` for the HTTP MCP host.
- `ModelContextProtocol` for the stdio MCP host.
- `MailKit` for IMAP and MIME parsing.
- `Microsoft.Extensions.Options.ConfigurationExtensions` and options validation.
- `Microsoft.AspNetCore.OpenApi` only if an operational API is documented; do not expose MCP tools as REST endpoints.
- `OpenTelemetry.Extensions.Hosting`.
- ASP.NET Core, HTTP, runtime, and process OpenTelemetry instrumentation.
- Azure Monitor OpenTelemetry exporter or the current supported Azure Monitor distribution.
- `Microsoft.Extensions.Http.Resilience` only if used for the future OAuth token endpoint; do not apply HTTP retry policies to IMAP.
- `xunit`, `FluentAssertions` or the repository's chosen assertion library, and `NSubstitute` or a minimal hand-written fake.
- `Microsoft.AspNetCore.Mvc.Testing` for HTTP host tests.
- `Testcontainers` with a pinned IMAP test server image for integration tests.
- `NetArchTest.Rules` or equivalent for dependency rules.

Do not add both multiple assertion libraries or multiple mocking frameworks.

### 7.3 Baseline commands

Run from `D:\source`:

```powershell
New-Item -ItemType Directory -Path .\YahooMailMcp
Set-Location .\YahooMailMcp
git init
dotnet new gitignore
dotnet new editorconfig
dotnet new slnx -n YahooMailMcp
dotnet new aspire-apphost -n YahooMailMcp.AppHost -o src/YahooMailMcp.AppHost
dotnet new aspire-servicedefaults -n YahooMailMcp.ServiceDefaults -o src/YahooMailMcp.ServiceDefaults
dotnet new classlib -n YahooMailMcp.Domain -o src/YahooMailMcp.Domain -f net10.0
dotnet new classlib -n YahooMailMcp.Application -o src/YahooMailMcp.Application -f net10.0
dotnet new classlib -n YahooMailMcp.Infrastructure.MailKit -o src/YahooMailMcp.Infrastructure.MailKit -f net10.0
dotnet new web -n YahooMailMcp.Server.Http -o src/YahooMailMcp.Server.Http -f net10.0
dotnet new console -n YahooMailMcp.Server.Stdio -o src/YahooMailMcp.Server.Stdio -f net10.0
dotnet new xunit -n YahooMailMcp.UnitTests -o tests/YahooMailMcp.UnitTests -f net10.0
dotnet new xunit -n YahooMailMcp.IntegrationTests -o tests/YahooMailMcp.IntegrationTests -f net10.0
dotnet new xunit -n YahooMailMcp.ArchitectureTests -o tests/YahooMailMcp.ArchitectureTests -f net10.0
dotnet new xunit -n YahooMailMcp.AspireTests -o tests/YahooMailMcp.AspireTests -f net10.0
```

If the installed Aspire release uses the Aspire CLI rather than these `dotnet new` template names, use the current stable equivalent and preserve the project names and responsibilities in this specification. Then add projects to the solution and add references according to the dependency direction. If `dotnet new slnx` is unsupported, create a traditional solution.

### 7.4 Aspire local application model

The AppHost is mandatory. It must model:

- `yahoo-mail-mcp-http`: project reference to `YahooMailMcp.Server.Http` with its HTTP endpoint and health checks.
- `imap-test`: a pinned local IMAP test container, enabled only for a `Testing` environment or explicit AppHost setting.
- Secret Aspire parameters for Yahoo email, Yahoo app password, MCP API key, and cursor signing key. Values come from AppHost user-secrets or environment variables and are passed as references; they must never be committed to AppHost configuration.
- A development profile that points the HTTP server at real Yahoo IMAP when the user intentionally supplies Yahoo credentials.
- A test profile that points the HTTP server at the local IMAP container and seeded deterministic account.

The HTTP server must call `builder.AddServiceDefaults()` and map the standard Aspire health endpoints alongside the explicit `/health/live` and `/health/ready` endpoints required by this specification. Avoid duplicate instrumentation and ensure neither endpoint leaks configuration.

The normal local workflow is:

```powershell
dotnet run --project ./src/YahooMailMcp.AppHost
```

This starts the AppHost, the HTTP MCP service, the selected dependencies, and the Aspire Dashboard. Developers must be able to inspect resource state, endpoints, structured logs, traces, and metrics without separately configuring an observability backend.

Aspire does not replace:

- The stdio server entry point used by local MCP clients.
- Unit tests or focused in-process HTTP tests.
- Docker image production.
- Terraform production infrastructure.
- Azure Monitor in deployed environments.

---

## 8. Domain and Application Contracts

### 8.1 Identifiers

Use IMAP folder full name plus UID as the stable message locator:

```csharp
public sealed record MailMessageId(string Folder, uint Uid, uint? UidValidity);
```

Always return `uidValidity` when known. A UID is only stable within a folder and UIDVALIDITY epoch. Moving a message can produce a new UID; the move result must return the destination identifier when Yahoo reports it.

Do not use sequence indexes as public identifiers.

### 8.2 Core interfaces

The Application project should define an interface equivalent to:

```csharp
public interface IYahooMailGateway
{
    Task<MailAccountStatus> GetStatusAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<MailFolderInfo>> ListFoldersAsync(CancellationToken cancellationToken);
    Task<PagedResult<MailMessageSummary>> ListMessagesAsync(ListMessagesRequest request, CancellationToken cancellationToken);
    Task<PagedResult<MailMessageSummary>> SearchMessagesAsync(SearchMessagesRequest request, CancellationToken cancellationToken);
    Task<MailMessageDetail> GetMessageAsync(GetMessageRequest request, CancellationToken cancellationToken);
    Task<MessageStateResult> SetReadStateAsync(SetReadStateRequest request, CancellationToken cancellationToken);
    Task<MessageStateResult> SetFlaggedStateAsync(SetFlaggedStateRequest request, CancellationToken cancellationToken);
    Task<MoveMessageResult> MoveMessageAsync(MoveMessageRequest request, CancellationToken cancellationToken);
}
```

There must be no delete method.

### 8.3 DTO rules

- Use explicit JSON property names in lower camel case.
- Dates are UTC ISO 8601 strings.
- Message size is bytes.
- Addresses expose display name and address in tool results but must not be logged.
- Bodies default to normalized plain text.
- HTML bodies are converted to text; raw HTML is not returned by default.
- Cap body output with `Mail__MaxBodyCharacters`, default `50_000`, hard maximum `200_000`.
- Return `bodyTruncated: true` when capped.
- Attachment metadata may include filename, media type, size, and content ID; attachment bytes are not returned.
- Return stable error objects with `code`, `message`, `retryable`, and optional safe `details`.

### 8.4 Pagination

Use opaque base64url cursors containing a versioned JSON payload with folder, UIDVALIDITY, direction, and last UID. Sign cursors with HMAC using `Cursor__SigningKey` in production so clients cannot alter search position or folder.

Never include credentials or message content in a cursor.

Default page size is 10, maximum is 50. Search maximum is 100 only if explicitly justified and bounded by server execution timeout.

---

## 9. MailKit IMAP Implementation

### 9.1 Configuration

Bind and validate:

```text
Yahoo__Host=imap.mail.yahoo.com
Yahoo__Port=993
Yahoo__UseSsl=true
Yahoo__Email=<secret>
Yahoo__AppPassword=<secret>
Yahoo__AuthenticationMode=AppPassword
Yahoo__ConnectionIdleTimeoutSeconds=240
Yahoo__CommandTimeoutSeconds=30
Yahoo__ReadRetryCount=1
Yahoo__TrashFolderDenyList=Trash,Bin,Deleted Items,Deleted Messages
```

Host and port may be overridden for integration tests. Production validation must require Yahoo's host, port 993, and SSL unless an explicit development environment flag is active.

### 9.2 Yahoo capability negotiation and Gmail incompatibilities

MailKit supports many standard and provider-specific IMAP extensions, including Gmail's `X-GM-EXT1`. Library support does not mean Yahoo supports the same extensions. Yahoo must be treated as its own IMAP provider.

After every successful authentication, build an immutable `YahooImapFeatureSet` from `ImapClient.Capabilities` and relevant folder responses. All optional operations must consult that feature set. Never force-enable a capability, infer Gmail behavior from MailKit APIs, or issue provider-specific raw commands.

Yahoo's published IMAP synchronization example advertises capabilities such as `IMAP4rev1`, `ENABLE`, `OBJECTID`, `CONDSTORE`, `QRESYNC`, `UIDONLY`, `PARTIAL`, and a message limit, while actual capabilities can vary by account and rollout. The capabilities returned by the live authenticated session are authoritative.

| Feature | Yahoo V1 behavior |
|---|---|
| Message listing and reading | Use standard UID-based `SEARCH`/`FETCH` and MIME retrieval through MailKit. Never expose sequence numbers as identifiers. |
| Read/unread and flagged state | Use only standard `Seen` and `Flagged` IMAP flags after opening the folder read-write. Detect and report read-only folders. |
| Standard folders | Discover with `LIST`; use `SPECIAL-USE` folder attributes when Yahoo returns them, otherwise apply a documented conservative folder-name fallback only where required. |
| Folder counts/status | Use `STATUS`/`LIST-STATUS` only when supported through MailKit; otherwise return available counts without opening every folder. |
| Gmail labels | Unsupported. Do not request or return `X-GM-LABELS`; Yahoo folders are not Gmail labels. |
| Gmail raw search | Unsupported. Never use `X-GM-RAW`; compose standard MailKit `SearchQuery` values. |
| Gmail message/thread IDs | Unsupported. Never use `X-GM-MSGID` or `X-GM-THRID`; use folder + UID + UIDVALIDITY. |
| Native move | Conditional. Enable only when `ImapCapabilities.Move` is advertised. Otherwise return `operation_not_supported`; never use MailKit's copy/delete fallback. |
| Server-side sort/thread | Conditional. Use only when advertised; V1 must work without `SORT` or `THREAD` by using bounded UID fetches and deterministic client-side ordering. |
| UIDPLUS/COPYUID | Conditional. Return a destination UID only when Yahoo supplies one; otherwise return a successful move with `destinationUid: null`. |
| CONDSTORE/QRESYNC | Optional optimization. Use only through supported MailKit APIs and keep baseline behavior correct without them. |
| OBJECTID, UIDONLY, PARTIAL | Do not depend on these in V1. If the selected MailKit version does not expose them safely, ignore them rather than sending raw commands. |
| Yahoo message limits | Respect advertised server limits such as `MESSAGELIMIT`; keep V1 request limits substantially below them and split bounded fetches when required. |
| Attachment search | Do not emulate Gmail `has:attachment`. Apply standard search filters first, then inspect body structure for a bounded candidate set when `hasAttachments` is requested. |
| Labels/categories | Do not expose a label API in V1. Folder moves and standard IMAP flags are the only organization primitives. |

Capability requirements:

- Record safe boolean capability telemetry, never raw server greetings or protocol logs.
- `yahoo_mail_status` may return safe booleans such as `nativeMoveSupported`, `specialUseSupported`, and `conditionalSyncSupported`.
- If capabilities change after reconnect, replace the feature set atomically.
- Tests must cover a minimal Yahoo profile, a richer documented Yahoo profile, and a deliberately Gmail-like profile to prove Gmail-only features remain disabled even when a test server advertises them.
- Do not expose MailKit's full capability enumeration as an MCP tool; keep provider internals narrow.

### 9.3 Authentication abstraction

Create:

```csharp
public interface IImapAuthenticator
{
    Task AuthenticateAsync(ImapClient client, CancellationToken cancellationToken);
}
```

`AppPasswordImapAuthenticator` calls `AuthenticateAsync(email, appPassword, cancellationToken)`.

Future `OAuthImapAuthenticator` depends on `IYahooAccessTokenProvider` and constructs the Yahoo-supported MailKit SASL mechanism. The gateway must not know where the token came from.

Never log authentication exceptions with raw provider responses if they can contain identifiers or tokens. Map them to `authentication_failed`.

### 9.4 Folder discovery

- Request personal namespace folders recursively.
- Include inbox.
- Map delimiter, full name, display name, attributes, unread count, total count, and subscription state.
- Use IMAP special-use attributes when available.
- Do not assume English folder names.
- Avoid opening every folder just to calculate counts; use status requests where supported.

### 9.5 Listing recent messages

- Open folder read-only.
- Use UID-oriented search/fetch.
- Sort newest first by internal date, with UID as deterministic tie-breaker.
- Fetch only required summary fields: UID, envelope, internal date, size, standard IMAP flags, and body structure for attachment metadata when requested.
- Never request Gmail label, Gmail message ID, or Gmail thread ID summary items, even if MailKit exposes those APIs.
- Do not download full MIME bodies during list operations.

### 9.6 Search

Expose structured filters rather than raw IMAP search syntax:

- `text`
- `from`
- `to`
- `subject`
- `sinceUtc`
- `beforeUtc`
- `unreadOnly`
- `flaggedOnly`
- `hasAttachments`
- `folder`
- `limit`
- `cursor`

Combine filters with AND. Reject invalid dates, empty searches that would scan every folder, unsupported cross-folder searches in V1, and limits above the maximum.

Use MailKit `SearchQuery` construction. Do not concatenate user input into raw IMAP commands. Implement `hasAttachments` as a bounded post-filter over fetched body structures; do not translate it to Gmail raw search syntax.

### 9.7 Reading a message

- Open the requested folder read-only.
- Validate UIDVALIDITY when supplied.
- Fetch by UID.
- Return envelope, dates, selected safe headers, flags, body text, body truncation status, and attachment metadata.
- Allow a `bodyMode` of `metadata`, `text`, or `headersAndText`.
- Exclude authentication-related and transport trace headers unless explicitly approved.
- Do not load attachment bodies.

### 9.8 State changes

Read/unread:

- Open folder read-write.
- Add or remove `MessageFlags.Seen` only.

Flag/unflag:

- Open folder read-write.
- Add or remove `MessageFlags.Flagged` only.

Move:

- Validate source and destination.
- Reject same-folder moves.
- Reject a destination with `FolderAttributes.Trash`.
- Reject canonical destination names in the deny-list, case-insensitively after trimming delimiter differences.
- Require `ImapCapabilities.Move` on the active authenticated session before calling MailKit.
- Call MailKit's high-level `MoveToAsync`/UID move API only when native `MOVE` is advertised.
- If native `MOVE` is absent, return `operation_not_supported`. Do not permit MailKit to fall back to COPY + `\\Deleted` + EXPUNGE because that conflicts with the no-delete invariant.
- Return source identifier, destination identifier when available, and completion status.
- Do not retry after an ambiguous connection failure.

### 9.9 Error mapping

Use these stable codes:

```text
authentication_failed
connection_failed
connection_timeout
folder_not_found
folder_read_only
message_not_found
uid_validity_changed
invalid_cursor
invalid_request
result_limit_exceeded
trash_destination_forbidden
operation_not_supported
provider_rate_limited
provider_error
operation_cancelled
service_unavailable
```

Provider exception text may be logged only after sanitization and without request content.

---

## 10. MCP Server and Tools

Use the official C# MCP SDK's attribute-based tool discovery. Use `ModelContextProtocol.AspNetCore` for Streamable HTTP and `ModelContextProtocol` for stdio. Prefer stateless HTTP session mode because this server does not need sampling or elicitation.

### 10.1 Tool design rules

- Tool names use `yahoo_mail_` prefix.
- Descriptions are concise, explicit about side effects, and state that deletion is unavailable.
- Inputs use JSON-schema-friendly primitive types and records.
- Every call accepts cancellation from the MCP runtime.
- Tool methods delegate to Application services and contain no MailKit code.
- Tool results are structured JSON-compatible objects, not prose-only blobs.
- Do not expose credentials, server capabilities that reveal sensitive topology, or stack traces.

### 10.2 Required tools

#### `yahoo_mail_status`

Purpose: Check configured account connectivity without returning secrets.

Input: none.

Output:

```json
{
  "connected": true,
  "authenticated": true,
  "account": "a***@example.com",
  "server": "imap.mail.yahoo.com",
  "nativeMoveSupported": false,
  "specialUseSupported": true,
  "conditionalSyncSupported": true,
  "deleteCapability": false
}
```

#### `yahoo_mail_list_folders`

Purpose: List available personal mail folders and safe metadata.

Input: optional `includeUnsubscribed`, default false.

#### `yahoo_mail_list_messages`

Purpose: List recent message summaries in one folder.

Inputs: `folder`, `limit`, `cursor`, `unreadOnly`, `flaggedOnly`.

Default folder is `INBOX`; default limit is 10.

#### `yahoo_mail_search_messages`

Purpose: Search one folder using structured filters.

Inputs: the search fields in section 9.5.

#### `yahoo_mail_get_message`

Purpose: Read metadata and a bounded text representation of one message.

Inputs: `folder`, `uid`, optional `uidValidity`, `bodyMode`, optional `maxBodyCharacters` bounded by server maximum.

#### `yahoo_mail_mark_read`

Purpose: Mark one message read. This changes only the IMAP `Seen` flag.

Inputs: `folder`, `uid`, optional `uidValidity`.

#### `yahoo_mail_mark_unread`

Purpose: Mark one message unread. This changes only the IMAP `Seen` flag.

Inputs: `folder`, `uid`, optional `uidValidity`.

#### `yahoo_mail_set_flagged`

Purpose: Set or clear the flagged state.

Inputs: `folder`, `uid`, optional `uidValidity`, `flagged`.

#### `yahoo_mail_move_message`

Purpose: Move one message to a validated non-trash folder only when Yahoo advertises native IMAP `MOVE`. The description must state that trash destinations are forbidden and that the operation can return `operation_not_supported` when native move is unavailable.

Inputs: `sourceFolder`, `uid`, optional `uidValidity`, `destinationFolder`.

### 10.3 Explicitly absent tools

There must be no tools for:

- Deleting or trashing.
- Emptying folders.
- Bulk cleanup.
- Sending or composing.
- Downloading attachments.
- Executing arbitrary IMAP searches or commands.
- Changing account settings.

### 10.4 HTTP transport

Use:

```csharp
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.SessionMode = HttpServerSessionMode.Stateless;
    })
    .WithToolsFromAssembly();
```

Map MCP at `/mcp`. Map health checks separately at `/health/live` and `/health/ready`.

Set exact `AllowedHosts` in production. Do not enable CORS unless a known browser client requires it; if enabled, allow only explicit origins. Do not use `*` for origins or hosts.

### 10.5 Stdio transport

Use `WithStdioServerTransport()` and the same tool assembly. Configure console logging so every diagnostic event goes to stderr. Never write banners or status lines to stdout.

---

## 11. Remote MCP Endpoint Authentication

Yahoo authentication and MCP-client authentication are separate concerns.

For the personal V1 remote deployment:

- Require either `Authorization: ApiKey <key>` or a validated Entra `Authorization: Bearer <access_token>` for `/mcp`.
- Store the token as `Mcp__BearerToken` in Key Vault and inject it through a Container Apps secret reference.
- Compare the presented token in constant time after fixed-form decoding, or compare a SHA-256 digest in constant time.
- Return `401` without revealing whether the token format or value was wrong.
- Do not accept the token in query strings.
- Exempt liveness/readiness and OAuth protected-resource metadata endpoints.
- Apply ASP.NET Core rate limiting per source IP and globally.
- Reject request bodies above a conservative configured limit.
- Enforce HTTPS at the public ingress.

The static API key remains a V1 personal access-control option. Entra OAuth is the
standards-based path for OAuth-capable remote clients such as ChatGPT: publish RFC
9728 protected-resource metadata, validate tenant issuer, signature, lifetime, and
the application-GUID audience, and require the delegated `access_as_user` scope.
Never fall back from an invalid JWT to API-key authentication. Do not conflate
Yahoo OAuth tokens with MCP endpoint tokens.

---

## 12. Configuration and Secrets

### 12.1 Non-secret configuration

Use `appsettings.json`, environment-specific files without credentials, and environment variables. Environment variables override files.

Recommended keys:

```text
ASPNETCORE_ENVIRONMENT
ASPNETCORE_URLS=http://+:8080
AllowedHosts=mcp.alanlima.cloud
Mcp__Path=/mcp
Mcp__AuthenticationMode=ApiKeyAndOAuth
Mcp__OAuth__Enabled=false
Mcp__OAuth__TenantId=<Entra tenant GUID>
Mcp__OAuth__ClientId=<Entra application GUID>
Mcp__OAuth__Audience=<Entra application GUID>
Mcp__OAuth__Resource=https://mcp.alanlima.cloud/mcp
Mcp__OAuth__RequiredScope=access_as_user
Mail__DefaultFolder=INBOX
Mail__DefaultPageSize=10
Mail__MaxPageSize=50
Mail__MaxBodyCharacters=50000
Mail__HardMaxBodyCharacters=200000
Mail__OperationTimeoutSeconds=45
Yahoo__Host=imap.mail.yahoo.com
Yahoo__Port=993
Yahoo__UseSsl=true
Yahoo__AuthenticationMode=AppPassword
Yahoo__ConnectionIdleTimeoutSeconds=240
Yahoo__CommandTimeoutSeconds=30
Yahoo__ReadRetryCount=1
Yahoo__TrashFolderDenyList=Trash,Bin,Deleted Items,Deleted Messages
Observability__ServiceName=yahoo-mail-mcp
Observability__EnableConsoleExporter=false
```

### 12.2 Secrets

```text
Yahoo__Email
Yahoo__AppPassword
Mcp__BearerToken
Cursor__SigningKey
```

The optional Entra MCP OAuth client credential is stored outside Terraform as
`mcp-oauth-client-secret`, but is consumed by ChatGPT during the authorization-code
exchange rather than by the MCP runtime. Future Yahoo mailbox OAuth adds:

```text
YahooOAuth__ClientId
YahooOAuth__ClientSecret
YahooOAuth__RefreshToken
YahooOAuth__RedirectUri
```

The email address is sensitive personal data even though it is not an authentication secret. Store it in Key Vault in production.

### 12.3 Local `.env`

Commit only `.env.example`:

```dotenv
YAHOO__EMAIL=your-address@yahoo.com
YAHOO__APPPASSWORD=replace-with-yahoo-app-password
MCP__BEARERTOKEN=replace-with-at-least-32-random-bytes
CURSOR__SIGNINGKEY=replace-with-at-least-32-random-bytes
```

Ensure `.env`, `.env.*` except `.env.example`, user secrets, test result attachments, Terraform state, override files, and local plans are gitignored.

### 12.4 Secret handling rules

- Never put secret values in Terraform variables or state.
- Terraform creates the vault, permissions, and versionless secret references, but a separate script or protected manual step writes secret values.
- `Set-KeyVaultSecrets.ps1` must read values from secure environment variables or prompt with `Read-Host -AsSecureString`; it must not print values.
- GitHub Actions must not receive Yahoo credentials unless a specific smoke-test environment is created later. Production runtime reads them through Container Apps Key Vault references.
- Rotate the app password and MCP API key independently.
- Versionless Key Vault references are preferred so Container Apps can pick up newer versions; document restart/refresh behavior and verify it in staging before relying on automatic rotation.

---

## 13. Observability

Use OpenTelemetry for logs, metrics, and traces. In local development, export through OTLP to the Aspire Dashboard. In Azure, export to Azure Monitor/Application Insights. Use structured logging with event IDs and one shared Service Defaults registration path to avoid duplicate providers.

### 13.1 Aspire local monitoring

- `YahooMailMcp.ServiceDefaults` configures baseline logging, tracing, metrics, service discovery, and health checks.
- The AppHost automatically supplies OTLP endpoint and resource identity settings to the HTTP server.
- The Aspire Dashboard is the required local view for resource health, endpoints, console logs, structured logs, traces, and metrics.
- The agent may use current Aspire CLI telemetry commands such as `aspire describe`, `aspire otel logs`, and `aspire otel traces` when available, preferably with structured JSON output.
- Local telemetry must obey the same privacy rules as Azure telemetry. Do not treat an in-memory local dashboard as permission to capture mail payloads or secrets.
- The AppHost must make it easy to distinguish the real-Yahoo profile from the local-IMAP test profile to prevent accidental mutation of a real mailbox.
- Aspire Dashboard authentication settings must remain secure. Do not enable unsecured anonymous dashboard access on a non-loopback interface.

### 13.2 Required telemetry

Metrics:

```text
yahoo_mail_mcp_tool_calls_total{tool,outcome}
yahoo_mail_mcp_tool_duration_ms{tool}
yahoo_mail_mcp_imap_operations_total{operation,outcome}
yahoo_mail_mcp_imap_operation_duration_ms{operation}
yahoo_mail_mcp_reconnects_total{reason}
yahoo_mail_mcp_auth_failures_total
yahoo_mail_mcp_rate_limit_rejections_total
yahoo_mail_mcp_active_requests
```

Trace spans:

- MCP request span.
- Application operation span.
- IMAP operation span with operation name only.
- Secret retrieval is platform-managed and should not be manually traced with values.

Safe span/log fields:

```text
tool.name
operation.name
outcome
error.code
retry.count
folder.hash
message.uid
message.uid_validity
result.count
body.truncated
mail.delete_capability=false
```

Hash folder names with a keyed or process-local strategy if they are needed for correlation. Do not record raw folder names because users may create folders containing personal data.

### 13.3 Forbidden telemetry

Do not record:

- Message subject or body.
- From/to/cc/bcc addresses or names.
- Attachment names or contents.
- Search text.
- Folder names.
- Yahoo email, app password, OAuth token, refresh token, client secret, MCP API key, cursor signing key.
- Full request/response payloads.
- MIME content or raw IMAP protocol traces in production.

Disable MailKit protocol logging by default. If a local troubleshooting mode is ever added, require explicit opt-in, redact authentication and message data, and prevent it from running in production.

### 13.4 Health checks

`/health/live`

- Confirms process health only.
- Does not contact Yahoo.
- Returns no configuration details.

`/health/ready`

- Confirms required configuration is present.
- May report cached IMAP readiness but must not authenticate to Yahoo on every probe.
- Returns generic failure details publicly; full safe diagnostics go to logs.

### 13.5 Alerts

Terraform should create alerts for:

- No healthy replicas for 5 minutes.
- HTTP 5xx or MCP internal error rate above threshold.
- Repeated Yahoo authentication failures.
- P95 request latency above the configured target.
- Container restarts above threshold.
- Key Vault access failures.

Send alerts to an Azure Monitor action group whose email/webhook receiver is supplied as a non-secret environment variable. Allow alerts to be disabled in development.

---

## 14. Security Design

### 14.1 Threats and controls

Credential theft:

- Key Vault references and managed identity.
- No credentials in GitHub, Terraform state, image layers, logs, or tool inputs.
- Non-root container and minimal image.

Unauthorized MCP access:

- TLS-only ingress.
- Strong random MCP API key in V1.
- Rate limiting.
- Optional ingress IP allow-list.
- Exact host filtering.

Prompt/tool misuse:

- Narrow structured tools.
- No raw IMAP commands.
- No delete or send capability.
- Server-side validation independent of tool description.

Data exfiltration:

- Bounded message bodies.
- No attachment download.
- No persistent mail store.
- No payload logging.
- Single account.

Resource exhaustion:

- Page and body limits.
- Request-body limit.
- Operation timeouts and cancellation.
- Serialized IMAP access.
- Rate limits and single replica.

DNS rebinding/host spoofing:

- Exact `AllowedHosts`.
- Azure ingress host validation.
- Restrictive or disabled CORS.

Supply chain:

- Lock NuGet dependencies.
- Dependabot.
- Pin GitHub Actions to commit SHAs.
- Build with deterministic settings.
- Generate SBOM and provenance/attestation where supported.
- Scan container and dependencies before release.

### 14.2 Container security

- Multi-stage build.
- Build stage uses the pinned .NET SDK image.
- Runtime uses `mcr.microsoft.com/dotnet/aspnet:10.0` pinned by digest for release.
- Run as a numeric non-root UID.
- Listen on 8080.
- Read-only root filesystem where Container Apps configuration supports it; otherwise ensure the app does not require writes except a bounded temp directory.
- Drop unnecessary Linux capabilities.
- Do not install shells, curl, or package managers in the final image unless required for a justified health probe.
- Use ASP.NET health checks rather than a shell-based Docker health command when the runtime image lacks tools.
- Set invariant globalization only if MIME parsing and internationalized mail remain correct; default is to keep globalization support.

### 14.3 Privacy

- Document that mail data is processed transiently to satisfy user requests.
- Do not persist mail data.
- Do not use mail content for advertising, profiling, model training, or analytics.
- Keep telemetry content-free.
- Provide a documented incident response and secret-rotation procedure.

---

## 15. Docker

### 15.1 Dockerfile

`src/YahooMailMcp.Server.Http/Dockerfile` must:

1. Use a .NET 10 SDK build stage.
2. Copy project files and package metadata before source files for restore caching.
3. Restore with the lock file.
4. Publish Release, framework-dependent, without apphost.
5. Copy output into a .NET 10 ASP.NET runtime image.
6. Set non-root user.
7. Expose 8080.
8. Set `ASPNETCORE_URLS=http://+:8080`.
9. Use `ENTRYPOINT ["dotnet", "YahooMailMcp.Server.Http.dll"]`.
10. Include OCI labels for source revision and image version through build arguments.

Do not pass secrets as build arguments or copy `.env`, tests, Terraform state, or repository metadata into the image.

### 15.2 `.dockerignore`

Exclude at minimum:

```text
.git
.github
.vs
.vscode
**/bin
**/obj
**/TestResults
.env
.env.*
!.env.example
**/*.tfstate*
**/.terraform
**/*.tfplan
work
```

### 15.3 Docker Compose

`docker-compose.yml` should run:

- `yahoo-mail-mcp` built from the HTTP Dockerfile.
- An optional `imap-test` profile using a pinned GreenMail or equivalent local IMAP image for integration testing.

Docker Compose is a secondary portability and CI aid. Aspire AppHost is the required primary local developer experience and should model the same HTTP service and test IMAP dependency without requiring developers to coordinate Compose manually.

Service requirements:

- Port mapping `127.0.0.1:8080:8080` by default.
- `env_file: .env` for local use only.
- Health check against `/health/live` using a method available in the final image, or Compose's TCP/HTTP facility if supported.
- Read-only filesystem where feasible.
- `tmpfs` for `/tmp`.
- `security_opt: no-new-privileges:true`.
- Resource limits suitable for a small personal service.
- No Docker socket mount.

Commands:

```powershell
Copy-Item .env.example .env
docker compose config
docker compose up --build
docker compose down
```

---

## 16. Terraform Infrastructure

Use Terraform with separate state for bootstrap, foundation, and application. Pin Terraform and provider version constraints, then commit `.terraform.lock.hcl` for each root module.

### 16.1 Providers

- `hashicorp/azurerm`
- `hashicorp/azuread`
- `hashicorp/aws`
- `hashicorp/random` only for non-secret resource suffixes; do not generate operational secrets into state
- `hashicorp/time` only if an eventual-consistency wait is proven necessary

Configure AzureRM with `features {}` and OIDC-compatible authentication. Configure AWS for Route 53 through GitHub OIDC or local AWS profile.

### 16.2 Microsoft standard Azure naming convention

All Azure resources must follow Microsoft's Cloud Adoption Framework (CAF) naming guidance and recommended resource abbreviations. Centralize name generation in Terraform locals; individual resources must consume those locals rather than constructing names independently.

Canonical components:

```text
workload    = ymcp
environment = dev | test | stage | prod
region      = aue for australiaeast by default; define an explicit validated map for every allowed Azure region
instance    = 001
```

Default readable format:

```text
<resource-abbreviation>-<workload>-<environment>-<region>-<instance>
```

Required matrix:

| Resource | CAF abbreviation | Production example |
|---|---:|---|
| Resource group | `rg` | `rg-ymcp-prod-aue-001` |
| Container App | `ca` | `ca-ymcp-prod-aue-001` |
| Container Apps environment | `cae` | `cae-ymcp-prod-aue-001` |
| User-assigned managed identity | `id` | `id-ymcp-prod-aue-001` |
| Log Analytics workspace | `log` | `log-ymcp-prod-aue-001` |
| Application Insights | `appi` | `appi-ymcp-prod-aue-001` |
| Azure Monitor action group | `ag` | `ag-ymcp-prod-aue-001` |
| Container registry | `cr` | `crymcpprodaue001<uniq>` |
| Key Vault | `kv` | `kv-ymcp-prod-aue-01<uniq>` |
| Terraform state storage account | `st` | `stymcptfprodaue001<uniq>` |
| Terraform state resource group | `rg` | `rg-ymcp-tfstate-aue-001` |

`<uniq>` is a stable, lowercase, non-secret four-character suffix generated once for globally scoped resources. It may be a `random_string` with keepers or a deterministic organization-approved suffix. It must not contain personal data, a secret, or a raw subscription/tenant identifier.

Naming implementation requirements:

- Use official CAF abbreviations: `rg`, `ca`, `cae`, `cr`, `kv`, `id`, `log`, `appi`, `ag`, and `st`.
- Use lowercase names unless the Azure resource explicitly permits and organizational policy requires otherwise.
- Use hyphens for readability where the resource allows them.
- Remove hyphens for ACR and Storage Account names, which require alphanumeric naming.
- Keep Key Vault within its current length and character limits; use the compact matrix format rather than truncating unpredictably.
- Validate workload, environment, region code, instance, and uniqueness suffix variables.
- Keep mutable metadata such as owner/team and cost center in tags, not names.
- Do not place personal, sensitive, or confidential information in names or tags.
- Use the same component order across bootstrap, foundation, and application stacks.
- Document any exception next to the Terraform local and link it to the Azure resource naming constraint that requires the exception.
- Add Terraform tests or assertions that compare generated names to the matrix and verify length/character limits before planning resources.

Required common tags:

```text
workload=yahoo-mail-mcp
environment=<environment>
region=<azure-region>
managed-by=terraform
repository=<non-secret repository identifier>
owner=<team or role, not a personal email address>
data-classification=confidential
```

### 16.3 Bootstrap stack

Purpose: one-time creation of the remote Terraform backend and optionally the deployment identities.

Resources:

- Azure resource group for Terraform state.
- Storage account with secure transfer required, public blob access disabled, TLS 1.2+, versioning, soft delete, and lifecycle protections.
- Private blob container for state.
- Optional Azure Entra application/service principal and federated identity credentials for GitHub environments if organizational policy permits Terraform to bootstrap them.
- Role assignments scoped to the least privilege required.

Bootstrap initially uses local state. After creation, migrate foundation and application state to Azure Blob. Never commit backend access keys; use Azure identity/OIDC.

### 16.4 Foundation stack

Resources:

- Resource group.
- Azure Container Registry with admin user disabled.
- Log Analytics workspace with explicit retention.
- Workspace-based Application Insights resource.
- Azure Container Apps managed environment linked to Log Analytics.
- Azure Key Vault using Azure RBAC, soft delete, purge protection, and public access policy appropriate to the deployment environment.
- User-assigned managed identity for the Container App.
- `AcrPull` role assignment for that identity on ACR.
- `Key Vault Secrets User` role assignment for that identity on Key Vault.
- Azure Monitor action group and baseline alerts.

Do not create secret values in this stack.

Foundation outputs should include vault name/URI, ACR login server, Container Apps environment ID, managed identity ID/client ID/principal ID, Application Insights connection string, and resource names. Mark any provider-declared sensitive output appropriately, even if not a credential.

### 16.5 Secret provisioning gate

After foundation apply, run:

```powershell
$env:YAHOO_EMAIL = "your-address@yahoo.com"
$env:YAHOO_APP_PASSWORD = "your-app-password"
$env:MCP_BEARER_TOKEN = "a-cryptographically-random-token"
$env:CURSOR_SIGNING_KEY = "a-separate-cryptographically-random-key"
./scripts/Set-KeyVaultSecrets.ps1 -VaultName <vault-name>
```

The script creates these Key Vault secret names:

```text
yahoo-email
yahoo-app-password
mcp-bearer-token
cursor-signing-key
```

Unset the shell environment variables after provisioning. The script must not echo values.

### 16.6 Application stack

Resources:

- Azure Container App with external HTTPS ingress.
- Port 8080.
- Single active revision mode.
- `min_replicas = 1`, `max_replicas = 1`.
- User-assigned identity attached.
- ACR registry configuration using that identity, not registry passwords.
- Versionless Key Vault secret references for the four runtime secrets.
- Environment variables referencing Container App secrets.
- Non-secret app configuration.
- CPU/memory defaults appropriate for a personal service, initially 0.5 vCPU and 1 GiB.
- Revision suffix derived from the immutable image tag or Git SHA.
- Route 53 DNS record.
- Custom domain and managed certificate binding when supported reliably by the current AzureRM provider; use `azapi` only for the narrow unsupported resource surface and document why.
- Diagnostic settings where the resource supports them and they add coverage beyond the managed environment integration.

Use an immutable image reference, preferably ACR repository plus digest. Never deploy `latest`.

### 16.7 Route 53 DNS

Assume the public endpoint is configurable, defaulting to `mcp.alanlima.cloud`.

- Accept `route53_zone_name` and `mcp_fqdn` variables.
- Find the existing public hosted zone with a data source.
- Create a CNAME from the MCP host to the Container App's default FQDN.
- Set a low TTL during initial certificate setup, then increase after validation.
- If using a zone apex, do not invent an invalid CNAME; require an explicit alternative design.
- Complete Azure custom-domain verification and certificate binding in dependency order.
- Output the final `https://<fqdn>/mcp` URL.

### 16.8 Terraform safeguards

- Use variable validation for names, locations, FQDN, image digest/tag, retention, and environment.
- Add `prevent_destroy` to production Key Vault, ACR, Log Analytics, and state storage where operationally appropriate.
- Tag every Azure resource with application, environment, owner, managed-by, and repository.
- Avoid broad subscription-level roles where resource-group or resource scope works.
- Do not output secrets.
- Run `terraform fmt`, `validate`, provider lock validation, and a security scanner in CI.
- Save production plans as protected workflow artifacts with short retention and review them before apply.

---

## 17. GitHub Actions OIDC CI/CD

Use GitHub environments named `development` and `production`. Production requires approval. Use OIDC for both Azure and AWS.

### 17.1 Required GitHub variables

Repository or environment variables:

```text
AZURE_CLIENT_ID
AZURE_TENANT_ID
AZURE_SUBSCRIPTION_ID
AZURE_LOCATION
AZURE_RESOURCE_GROUP
AZURE_CONTAINER_APP_NAME
AZURE_CONTAINER_REGISTRY_NAME
AWS_ROLE_TO_ASSUME
AWS_REGION
TF_STATE_RESOURCE_GROUP
TF_STATE_STORAGE_ACCOUNT
TF_STATE_CONTAINER
MCP_FQDN
ROUTE53_ZONE_NAME
```

These identifiers are not passwords. Keep actual secrets out of GitHub for the normal pipeline.

### 17.2 Azure federation

Create a dedicated Entra application/service principal for GitHub deployment, or a user-assigned identity where supported by the login flow. Add federated credentials restricted to the exact GitHub organization, repository, and environment subject.

Use separate federated credentials for pull-request planning and protected production deployment. Grant only the roles needed for resource management and ACR push at the narrowest practical scopes.

### 17.3 AWS federation

Create an AWS IAM OIDC provider for `token.actions.githubusercontent.com` and a Route 53 deployment role. Its trust policy must restrict:

- Audience to `sts.amazonaws.com`.
- Subject to the exact GitHub repository and protected production environment or branch.

Grant only `route53:ChangeResourceRecordSets` for the target hosted zone plus the minimum read actions needed to discover and verify the zone/change.

### 17.4 Workflow permissions

Default to:

```yaml
permissions:
  contents: read
```

Jobs using OIDC add:

```yaml
permissions:
  contents: read
  id-token: write
```

Do not grant `write-all`.

### 17.5 `ci.yml`

Triggers: pull requests and pushes to the default branch.

Jobs:

1. Restore with lock file.
2. Build Release with warnings as errors.
3. Unit tests with coverage.
4. Architecture/no-delete tests.
5. Integration tests using a local IMAP test container.
6. Aspire closed-box tests that start AppHost with the local IMAP profile and randomized ports.
7. `dotnet format --verify-no-changes`.
8. `Check-NoDelete.ps1`.
9. Docker build.
10. Container vulnerability scan; fail on fixable critical/high issues according to documented policy.
11. Generate SBOM.
12. Terraform format and validate for every root module.
13. Terraform naming assertions/tests for CAF abbreviations, component order, and resource-specific constraints.
14. Terraform lint/security scan.

Do not connect to Yahoo in CI.

### 17.6 `infrastructure.yml`

Triggers:

- Pull request paths under `infra/**`: OIDC-authenticated plan only.
- Manual dispatch: protected apply.

Plan job:

- Authenticate to Azure with OIDC.
- Authenticate to AWS with OIDC when the application stack includes DNS.
- Initialize remote backend.
- Validate and plan.
- Upload a human-readable plan summary and binary plan with short retention.
- Never post potentially sensitive full state to pull-request comments.

Apply job:

- Runs only from the default branch and production environment.
- Uses the reviewed plan artifact when practical and safe.
- Applies foundation before the secret provisioning gate.
- Applies application only after required Key Vault secrets exist.

### 17.7 `release.yml`

Triggers: push of `v*` tag or protected manual dispatch.

Steps:

1. Run or require successful CI.
2. Authenticate to Azure using OIDC.
3. Log in to ACR without admin credentials.
4. Build the image once.
5. Tag with Git SHA and semantic version when present.
6. Scan image.
7. Push immutable tags.
8. Capture image digest.
9. Generate and publish SBOM/provenance where supported.
10. Run application Terraform plan using the digest.
11. Apply through the protected production environment.
12. Poll liveness/readiness and perform an authenticated MCP protocol smoke test that lists tools but does not call Yahoo.
13. Do not run the live Yahoo smoke test automatically.

Pin every third-party action to a full commit SHA and annotate the intended release version in a comment.

---

## 18. Testing Strategy

### 18.1 Unit tests

Test:

- Option validation.
- Cursor encode/decode, tamper rejection, expiry/version handling.
- Page and body limit enforcement.
- Address and DTO mapping.
- Search filter construction through an abstraction or inspectable builder.
- Folder canonicalization.
- Trash detection using special-use attributes and deny-list names.
- Same-folder move rejection.
- UIDVALIDITY mismatch behavior.
- Provider exception-to-error-code mapping.
- Log redaction helpers.
- Bearer-token comparison and authentication middleware.
- Read-only retry policy and no retry for ambiguous mutations.
- Yahoo capability projection into `YahooImapFeatureSet`.
- Gmail-only capabilities remain disabled for Yahoo even when a fixture advertises `X-GM-EXT1`.
- Native move is rejected when `ImapCapabilities.Move` is absent.
- Server-side sort/thread fallbacks and bounded attachment post-filtering.

### 18.2 Architecture tests

Test:

- Domain references no infrastructure/server/MCP packages.
- Application references no MailKit or server packages.
- MCP tool classes depend on Application services, not `ImapClient`.
- Production assemblies expose no method with forbidden delete/trash/purge/expunge names.
- No production assembly references a dedicated raw IMAP command wrapper.
- Gateway interface contains no delete method.
- No tool descriptor includes a deletion capability.

### 18.3 Integration tests

Use Testcontainers with a local IMAP server seeded with deterministic MIME messages.

Cover:

- TLS/test connection as configured.
- Authentication success and failure.
- Folder listing.
- Newest-10 ordering.
- Structured search.
- Unicode subjects, display names, and body text.
- Multipart alternative plain-text selection.
- HTML-to-text fallback.
- Attachment metadata without bytes.
- Body truncation.
- Mark read/unread.
- Flag/unflag.
- Move to a normal folder when the test capability profile advertises native `MOVE`.
- Reject a normal-folder move as unsupported when native `MOVE` is absent, without changing source or destination state.
- Reject move to trash before mutation.
- Cancellation and timeout.
- Reconnect after server restart for a read operation.
- Capability-profile behavior for minimal Yahoo, richer Yahoo, and Gmail-like test servers.
- Absence of Gmail labels, raw search, thread IDs, and message IDs in all Yahoo DTOs and commands.

If the test server's move semantics differ from Yahoo, document the gap and keep a manual Yahoo smoke test.

### 18.4 MCP contract tests

Start each server in-process or as a child process using the official MCP client:

- Initialize protocol session.
- List tools and compare exact expected names.
- Assert forbidden tool names are absent.
- Validate input schemas.
- Call read-only tools against the test IMAP server.
- Call state tools and verify IMAP state.
- Verify structured error payloads.
- Verify HTTP authentication rejects missing/invalid tokens.
- Verify stdio stdout contains only protocol frames.

### 18.5 Aspire closed-box tests

Use `Aspire.Hosting.Testing` and `DistributedApplicationTestingBuilder<Projects.YahooMailMcp_AppHost>` to start the AppHost and its resources as separate processes.

Cover:

- AppHost starts the HTTP service and test IMAP dependency.
- Resource health reaches the expected state within a bounded timeout.
- Test ports are randomized so suites can run concurrently.
- The test obtains the HTTP endpoint by Aspire resource name rather than hard-coded port.
- Liveness, readiness, MCP authentication, initialization, and tool listing pass through the orchestrated endpoint.
- A representative read-only MCP call reaches the seeded IMAP server.
- OpenTelemetry logs/traces are emitted and contain no seeded message subject, address, body, folder name, or credentials.
- AppHost cleanup stops child processes and containers.

Keep the dashboard disabled by default in automated tests, as Aspire testing does. Permit a local diagnostic switch to enable it for troubleshooting.

### 18.6 Live Yahoo smoke test

Opt-in only, run manually:

```powershell
$env:YAHOO__EMAIL = "..."
$env:YAHOO__APPPASSWORD = "..."
./scripts/Invoke-LiveSmokeTest.ps1
```

The script may:

- Authenticate.
- List folders.
- Fetch the latest 10 summaries from INBOX.
- Capture and report a safe boolean Yahoo capability summary, including native `MOVE`, `SPECIAL-USE`, and conditional synchronization support.
- Read one selected message only after explicit confirmation.

The default smoke test must not mutate, move, flag, mark, delete, trash, or send anything. It must redact addresses and subjects from console output unless a local `-ShowPersonalData` switch is explicitly supplied.

### 18.7 Coverage and quality gates

- High coverage on Application policies and security-critical middleware.
- Every no-delete safeguard has a test.
- No requirement for arbitrary 100% coverage.
- Mutation testing is recommended for trash-policy and authentication checks if practical.

---

## 19. Documentation Requirements

`README.md` must include:

- What the service does and does not do.
- Prominent no-delete/no-send statement.
- Prerequisites.
- Yahoo app-password setup at a high level, linking to Yahoo's current official instructions rather than duplicating potentially stale account UI steps.
- Local stdio setup.
- Aspire AppHost setup, profiles, Dashboard usage, and telemetry inspection.
- Local Docker setup.
- MCP client examples using environment placeholders.
- Test commands.
- Deployment overview.
- Security disclosure contact/process.

`docs/architecture.md`

- Component responsibilities and dependency diagram.
- Aspire local topology and its boundary from Terraform production deployment.
- Connection lifecycle.
- UID/UIDVALIDITY behavior.
- Yahoo capability matrix and explicit Gmail incompatibilities.

`docs/authentication.md`

- V1 app-password model.
- Remote MCP API key and Entra OAuth access tokens.
- Future Yahoo OAuth design.
- Clear statement that Yahoo OAuth and MCP endpoint auth are separate.

`docs/deployment.md`

- Bootstrap, foundation, secret gate, application, DNS, certificate, and verification steps.
- CAF naming components, abbreviations, examples, and global-name exceptions.

`docs/operations.md`

- Health checks.
- Logs/queries.
- Alerts.
- Deploy and rollback.
- Secret rotation.
- Yahoo auth failure troubleshooting.

`docs/security.md` and `docs/threat-model.md`

- Data classification.
- Trust boundaries.
- Threats and mitigations.
- No-delete enforcement.
- Incident response.

---

## 20. Implementation Phases

The coding agent should complete phases in order and keep the repository buildable after each phase.

### Phase 0 — Scaffold and guardrails

Deliver:

- Repository and solution structure.
- Aspire AppHost and Service Defaults projects.
- `AGENTS.md`, build props, package management, formatting, gitignore.
- Domain/Application references.
- Initial CI with build and tests.
- No-delete source scan.

Exit criteria:

- Clean restore/build/test.
- Dependency architecture tests pass.
- No credentials or generated artifacts committed.

### Phase 1 — MailKit read-only core

Deliver:

- Validated configuration.
- App-password authenticator.
- Serialized IMAP session.
- Runtime Yahoo capability negotiation and explicit Gmail-extension exclusions.
- Status, folder list, recent list, search, and message read operations.
- Unit/integration tests.

Exit criteria:

- Local IMAP integration suite passes.
- Optional Yahoo smoke test returns the latest 10 summaries.
- No mutation code exists.

### Phase 2 — Safe organization operations

Deliver:

- Mark read/unread.
- Flag/unflag.
- Validated non-trash move.
- Trash policy and architecture safeguards.

Exit criteria:

- Normal moves pass integration tests when native IMAP `MOVE` is advertised.
- Sessions without native `MOVE` return `operation_not_supported` without invoking copy/delete fallback behavior.
- Trash moves fail before mutation.
- Forbidden API source scan and architecture tests pass.

### Phase 3 — MCP transports

Deliver:

- Stdio MCP server.
- Streamable HTTP MCP server.
- All required tool schemas.
- Remote bearer authentication.
- Health checks and rate limits.
- MCP contract tests.

Exit criteria:

- Official MCP client can list and call tools over both transports.
- Forbidden tools are absent.
- HTTP endpoint rejects unauthorized requests.

### Phase 4 — Container and local operations

Deliver:

- Hardened Dockerfile.
- Docker Compose.
- Aspire AppHost local topology, Dashboard telemetry, and test IMAP profile.
- Aspire closed-box integration tests.
- Local run documentation.
- Container tests and vulnerability scan.

Exit criteria:

- Image runs as non-root.
- Compose health check passes.
- AppHost starts the service and dependency profile, and the Aspire Dashboard receives privacy-safe telemetry.
- Aspire closed-box tests pass with randomized ports.
- MCP contract test passes against the container.

### Phase 5 — Terraform foundation

Deliver:

- Bootstrap and foundation stacks.
- Remote state.
- ACR, Container Apps environment, Key Vault, identity, Monitor.
- Central CAF-compliant Azure naming locals, validation, and naming tests.
- Secret provisioning script.

Exit criteria:

- Format/validate/security checks pass.
- Foundation plan contains no secret values.
- Managed identity has only required roles.

### Phase 6 — Deployment, DNS, and CI/CD

Deliver:

- Application stack.
- Route 53 record and custom domain.
- OIDC trust for Azure and AWS.
- CI, infrastructure, and release workflows.
- Deployment/rollback runbook.

Exit criteria:

- Immutable image deploys.
- Public HTTPS endpoint is healthy at configured FQDN.
- Authenticated MCP tool listing succeeds.
- Unauthorized calls fail.
- No long-lived Azure/AWS credentials exist in GitHub.

### Phase 7 — Production readiness

Deliver:

- Dashboards and alerts.
- Threat model review.
- Secret rotation drill.
- Live Yahoo read-only smoke test.
- Final documentation and acceptance report.

Exit criteria:

- All acceptance criteria below have evidence.

### Phase 8 — Future OAuth spike, not part of V1 completion

Deliver only after Yahoo approval:

- OAuth authorization flow.
- Token storage and refresh.
- MailKit SASL OAuth authentication.
- Migration path from app password.
- Revocation and incident runbook.

Keep this work behind a feature/configuration mode and preserve app-password support until migration is proven.

---

## 21. Acceptance Criteria

### Functional

- [ ] A configured user can connect to Yahoo IMAP over TLS 993 using an app password.
- [ ] `yahoo_mail_list_messages` returns the newest 10 INBOX summaries by default.
- [ ] Folders can be listed without opening every folder unnecessarily.
- [ ] Structured search supports the documented filters and bounded pagination.
- [ ] A message can be read by folder and UID with bounded text output.
- [ ] Read/unread and flagged state can be changed.
- [ ] A message can move to a normal destination folder when the authenticated Yahoo session advertises native IMAP `MOVE`.
- [ ] If Yahoo does not advertise native `MOVE`, the move tool returns `operation_not_supported` and performs no copy/delete fallback.
- [ ] A move to Trash or configured trash aliases is rejected before mutation.
- [ ] Yahoo operations are capability-negotiated and do not use Gmail labels, `X-GM-RAW`, `X-GM-MSGID`, or `X-GM-THRID`.
- [ ] No deletion, trash, purge, expunge, send, compose, reply, forward, or attachment-download tool exists.
- [ ] Stdio and Streamable HTTP transports expose the same tool set.

### Security

- [ ] Remote `/mcp` requires an API key or scoped Entra access token and HTTPS.
- [ ] Secrets exist only in local secure environment/user-secrets or Azure Key Vault.
- [ ] Terraform state contains no Yahoo/MCP/cursor secret values.
- [ ] Container runs as non-root and contains no `.env` or source secrets.
- [ ] Logs/traces contain no message content, subjects, addresses, folder names, searches, or secrets.
- [ ] Host filtering is exact and CORS is absent or restrictive.
- [ ] Architecture and source-scan tests enforce no-delete rules.
- [ ] GitHub Actions use OIDC for Azure and AWS and pin actions by SHA.

### Reliability

- [ ] Read operations recover from one transient disconnected session.
- [ ] Ambiguous state-changing operations are not retried automatically.
- [ ] Cancellation and timeouts propagate to MailKit.
- [ ] Health endpoints do not overload or repeatedly authenticate to Yahoo.
- [ ] Container Apps runs exactly one replica in V1.
- [ ] The Aspire AppHost is the default local entry point and exposes resource health plus privacy-safe logs, traces, and metrics in the local Dashboard.

### Quality

- [ ] Restore, build, tests, formatter check, Docker build, and Terraform validation pass.
- [ ] MCP contract tests pass over HTTP and stdio.
- [ ] Aspire closed-box tests start the full local topology and pass without fixed ports.
- [ ] Integration tests run without a real Yahoo account.
- [ ] Live Yahoo smoke test remains opt-in and read-only by default.
- [ ] README and operational/security documentation are complete.

### Deployment

- [ ] ACR admin credentials are disabled.
- [ ] Every Azure resource name follows the documented CAF abbreviation/component matrix and resource-specific character/length constraints.
- [ ] Container App pulls from ACR with managed identity.
- [ ] Container App reads secrets through Key Vault references with managed identity.
- [ ] Azure Monitor receives content-safe telemetry.
- [ ] Route 53 resolves the configured domain to Container Apps.
- [ ] Managed TLS certificate is valid.
- [ ] Release deploys an immutable image digest and supports rollback to a previous revision/image.

---

## 22. Command Runbook

### Local validation

```powershell
Set-Location D:\source\YahooMailMcp
dotnet tool restore
dotnet restore --locked-mode
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build --collect:"XPlat Code Coverage"
dotnet test ./tests/YahooMailMcp.AspireTests/YahooMailMcp.AspireTests.csproj --configuration Release --no-build
dotnet format --verify-no-changes
./scripts/Check-NoDelete.ps1
```

### Run the local topology with Aspire

```powershell
dotnet run --project ./src/YahooMailMcp.AppHost
```

Open the secure Aspire Dashboard URL printed by the AppHost. Use it to inspect resource state, endpoints, health, structured logs, traces, and metrics. Select the explicit local-IMAP test profile unless intentionally validating a real Yahoo account.

### Run stdio server

Configure secrets through environment variables or .NET user-secrets, then configure the MCP client to launch:

```powershell
dotnet run --project ./src/YahooMailMcp.Server.Stdio --configuration Release
```

Do not manually type into the stdio process; use an MCP client or inspector.

### Run HTTP server directly

```powershell
dotnet run --project ./src/YahooMailMcp.Server.Http --configuration Release
```

Direct execution is supported for focused debugging, but Aspire AppHost is the normal local development entry point.

Local endpoint:

```text
http://localhost:8080/mcp
```

### Docker

```powershell
docker build -f ./src/YahooMailMcp.Server.Http/Dockerfile -t yahoo-mail-mcp:local .
docker compose config
docker compose up --build
docker compose down
```

### Terraform bootstrap

```powershell
terraform -chdir=infra/terraform/bootstrap init
terraform -chdir=infra/terraform/bootstrap fmt -check
terraform -chdir=infra/terraform/bootstrap validate
terraform -chdir=infra/terraform/bootstrap plan -out=bootstrap.tfplan
terraform -chdir=infra/terraform/bootstrap apply bootstrap.tfplan
```

### Terraform foundation

```powershell
terraform -chdir=infra/terraform/foundation init -backend-config=../environments/prod/backend.hcl
terraform -chdir=infra/terraform/foundation fmt -check
terraform -chdir=infra/terraform/foundation validate
terraform -chdir=infra/terraform/foundation plan -var-file=../environments/prod/foundation.tfvars -out=foundation.tfplan
terraform -chdir=infra/terraform/foundation apply foundation.tfplan
```

Provision secrets after foundation and before application.

### Terraform application

```powershell
terraform -chdir=infra/terraform/application init -backend-config=../environments/prod/backend.hcl
terraform -chdir=infra/terraform/application fmt -check
terraform -chdir=infra/terraform/application validate
terraform -chdir=infra/terraform/application plan -var-file=../environments/prod/application.tfvars -out=application.tfplan
terraform -chdir=infra/terraform/application apply application.tfplan
```

Use unique backend keys for foundation and application state; do not point both stacks at the same state blob.

### Post-deployment verification

```powershell
Invoke-WebRequest https://mcp.alanlima.cloud/health/live
Invoke-WebRequest https://mcp.alanlima.cloud/health/ready
```

Then use an MCP inspector/client with the API key or an Entra access token to initialize and list tools. Confirm the exact expected tool set and absence of deletion tools before invoking any mail operation.

---

## 23. Rollback and Incident Procedures

### Deployment rollback

- Keep previous Container Apps revisions available according to retention policy.
- Roll traffic back to the last known-good immutable image/revision.
- Do not roll back Key Vault secret versions automatically unless the incident is proven to be a bad rotation.
- Record image digest, Terraform commit, and revision name in the deployment summary.

### Suspected credential exposure

1. Revoke the Yahoo app password immediately in Yahoo account security.
2. Rotate the MCP API key.
3. Rotate the cursor signing key; accept that existing cursors become invalid.
4. Inspect content-safe access and authentication logs.
5. Deploy/restart the active revision so new secret versions are loaded.
6. Review repository history, workflow logs, Terraform state, container layers, and artifacts for exposure.
7. If OAuth is later enabled, revoke refresh tokens and rotate the OAuth client secret as applicable.

### Unexpected mail mutation

1. Disable public ingress or set Container App replicas to zero.
2. Revoke the MCP API key and OAuth client credential.
3. Preserve logs and deployment metadata.
4. Inspect only the relevant operation paths.
5. Do not add cleanup or deletion behavior as part of incident response.
6. Restore service only after no-delete tests and a focused review pass.

---

## 24. Future Yahoo OAuth/OAUTHBEARER Path

Design now, implement later.

### 24.1 Components

```text
YahooOAuthOptions
IYahooAuthorizationService
IYahooAccessTokenProvider
IRefreshTokenStore
OAuthImapAuthenticator
TokenRefreshBackgroundService (optional)
```

### 24.2 Requirements

- Authorization code flow using Yahoo's current supported parameters.
- PKCE when supported for the registered application type.
- Exact redirect URI matching.
- State and nonce validation where applicable.
- Least-privilege approved Yahoo Mail scope.
- Refresh tokens encrypted at rest in Key Vault or a purpose-built encrypted store.
- Token endpoint calls through a typed `HttpClient` with bounded retries only for safe transient failures.
- Refresh synchronization so concurrent IMAP calls do not stampede the token endpoint.
- Proactive refresh with clock skew.
- Revocation handling and reauthorization status.
- No access/refresh token logs.
- MailKit authentication through a provider-selected SASL mechanism.
- Integration tests against a fake OAuth server plus an explicit approved-Yahoo smoke test.

### 24.3 Migration

- Add `Yahoo__AuthenticationMode=AppPassword|OAuth`.
- Keep both authenticators behind `IImapAuthenticator`.
- Deploy OAuth mode to a non-production environment first.
- Validate folder listing and latest-10 read-only flow.
- Switch production only after refresh and restart behavior is proven.
- Remove the app password from Key Vault only after a rollback window.

---

## 25. Ready-to-Use Codex / Coding Agent Prompt

Give the following prompt to Codex from `D:\source`. The implementation specification must already exist at `D:\source\YahooMailMcp\IMPLEMENTATION_SPEC.md`.

```text
Build the Yahoo Mail MCP project in D:\source\YahooMailMcp end-to-end according to IMPLEMENTATION_SPEC.md.

Start by reading IMPLEMENTATION_SPEC.md and creating the root AGENTS.md exactly as specified. Treat the no-delete rule as the highest-priority product invariant: do not expose or call delete, trash, purge, expunge, raw IMAP, send, draft, reply, forward, or attachment-download capabilities. A normal move is allowed only after the destination is validated as non-trash and Yahoo advertises native IMAP MOVE; MailKit's emulated copy/delete fallback is forbidden.

Implement phases 0 through 7 in order. Keep the solution buildable after each phase. Use current stable package versions compatible with .NET 10, central package management, lock files, Aspire, the official C# MCP SDK, and MailKit. Build an Aspire AppHost and Service Defaults project; make AppHost the default local entry point with Dashboard observability and closed-box Aspire tests. Build both stdio and stateless Streamable HTTP transports over the same application services. V1 uses a Yahoo app password; create the authentication abstraction and documentation for future Yahoo OAuth without implementing or pretending to validate restricted Yahoo OAuth access.

Create all source, tests, Aspire orchestration, Docker, Docker Compose, Terraform, GitHub Actions OIDC workflows, scripts, and documentation described by the specification. Never place secret values in source, examples, Terraform/state, image layers, workflow files, test fixtures, snapshots, or logs. Terraform must create Azure Container Apps, ACR, Key Vault, managed identity/RBAC, Azure Monitor resources, and Route 53 DNS, while secret values are provisioned separately after the foundation stack. Generate every Azure resource name from centralized Terraform locals using the Microsoft Cloud Adoption Framework abbreviations and naming matrix in the specification.

Treat MailKit as a protocol library, not proof that Yahoo supports every MailKit feature. Negotiate capabilities after Yahoo authentication. Never use Gmail-only X-GM-EXT1 labels, raw search, Gmail message IDs, or Gmail thread IDs. Only call MailKit move APIs when Yahoo advertises native IMAP MOVE; otherwise return operation_not_supported and never allow copy/delete/expunge fallback behavior.

Work autonomously on safe local changes and validation. Preserve unrelated existing changes. Use a focused plan and update it as phases complete. Add tests with each behavior. Run the narrowest tests first, then the complete validation suite in AGENTS.md. Do not run live Yahoo tests unless credentials are already available and the user explicitly authorizes that opt-in test. Do not apply Terraform, push images, alter DNS, create cloud resources, or make other external production changes without explicit approval.

When a package API, Terraform resource schema, or cloud workflow detail may have changed, check its current primary documentation before implementing it. Pin GitHub Actions to full commit SHAs and container images by digest before production release.

Completion requires evidence for every V1 acceptance criterion that can be verified locally. If cloud credentials, Yahoo credentials, approvals, Docker, or Terraform are unavailable, finish all remaining local work and provide exact commands and prerequisites for the unverified steps. Finish with a concise report of delivered phases, key files, validation results, remaining manual actions, and risks. Do not claim deployment or live Yahoo connectivity without evidence.
```

---

## 26. Implementation Notes for the Agent

- Begin with a read-only vertical slice: configuration -> MailKit gateway -> application operation -> MCP tool -> test.
- Add mutations only after read-only behavior is stable and the trash policy is tested.
- Keep the tool surface intentionally small. New tools require explicit product approval and a no-delete review.
- Use current primary documentation when package or Terraform APIs differ from examples in this specification.
- Avoid silently weakening requirements because a local dependency is unavailable. Implement what can be implemented, document the constraint, and leave reproducible validation commands.
- Treat `mcp.alanlima.cloud` as the default example, not a hard-coded host. Make it a Terraform/application variable.
- Keep the infrastructure split so secrets can be provisioned after Key Vault exists without entering Terraform state.
- Do not let a health probe trigger repeated Yahoo authentication.
- Do not return full MIME messages or raw provider exceptions.

---

## 27. Primary References

Use these as starting points and re-check them during implementation because SDKs and cloud provider schemas evolve:

- [Official MCP C# SDK getting started](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/getting-started.md)
- [Official MCP C# SDK repository](https://github.com/modelcontextprotocol/csharp-sdk)
- [Aspire AppHost overview](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/app-host-overview)
- [Aspire telemetry and local Dashboard](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/telemetry)
- [Aspire testing overview](https://learn.microsoft.com/en-us/dotnet/aspire/testing/overview)
- [Yahoo OAuth 2.0 guide](https://developer.yahoo.com/oauth2/guide/)
- [Yahoo Sign In and restricted-scope notice](https://developer.yahoo.com/sign-in-with-yahoo/)
- [Yahoo/AOL IMAP pagination and mail synchronization capability example](https://senders.yahooinc.com/static/Yahoo_Aol%20IMAP%20Pagination%20and%20Mail%20Sync-7564bd8d996168f4b38eee1440784515.pdf)
- [MailKit repository and documentation](https://github.com/jstedfast/MailKit)
- [Microsoft CAF Azure resource naming guidance](https://learn.microsoft.com/en-us/azure/cloud-adoption-framework/ready/azure-best-practices/resource-naming)
- [Microsoft CAF Azure resource abbreviation recommendations](https://learn.microsoft.com/en-us/azure/cloud-adoption-framework/ready/azure-best-practices/resource-abbreviations)
- [Azure Container Apps Key Vault secret references](https://learn.microsoft.com/en-us/azure/container-apps/manage-secrets)
- [Azure Container Apps Terraform resource](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/container_app)
- [GitHub Actions OIDC with Azure](https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure-openid-connect)
- [GitHub Actions OIDC in AWS](https://docs.aws.amazon.com/IAM/latest/UserGuide/id_roles_providers_create_oidc.html)
- [OpenAI model prompting guidance for coding agents](https://developers.openai.com/api/docs/guides/latest-model)

The official C# SDK currently identifies `ModelContextProtocol.AspNetCore` as the HTTP-server package and recommends stateless HTTP mode for servers that do not require server-to-client sampling or elicitation. Aspire supplies AppHost orchestration, Dashboard telemetry, and `Aspire.Hosting.Testing` for closed-box local tests. Azure Container Apps supports Key Vault references through managed identity and recommends the `Key Vault Secrets User` role. Microsoft CAF recommends resource-type abbreviations and stable workload/environment/region/instance components. Verify exact API and Terraform syntax against current docs when implementing.

---

## 28. Definition of Done

The project is done when phases 0–7 are implemented, all locally applicable validation passes, the no-delete controls have automated evidence, the deployment artifacts are reviewable and secret-free, and remaining external steps are explicit.

Production deployment is done only when the user has approved cloud changes and there is evidence of:

- Successful OIDC-authenticated release.
- Immutable image deployment.
- Healthy custom HTTPS endpoint.
- Authenticated MCP tool discovery.
- Rejected unauthorized MCP request.
- Read-only Yahoo live smoke test.
- Confirmed absence of delete/send tools.
- Content-safe telemetry in Azure Monitor.

Do not treat generated code alone as production completion.
