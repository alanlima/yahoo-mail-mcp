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
- Register configuration-bound infrastructure through `IHostApplicationBuilder` extensions and `BindConfiguration`. Never pass or capture an `IConfiguration` instance in service-registration APIs; configuration providers such as Azure App Configuration and Key Vault may be composed later in host setup.
- Use C# 14 `extension` declarations for extension members instead of the legacy `this` parameter syntax.
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
