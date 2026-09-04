# V1 acceptance report

## Current and next

Current: phases 0–7 and the dual API-key/Entra OAuth HTTP boundary are implemented.
The local build, automated contract/security suites, formatter, no-delete scan,
container build, Terraform validation, and Docker-backed GreenMail integration have
been exercised successfully. Per the final handoff instruction, no additional
runtime test investigation was performed after the last documentation pass.

Next: commit the source, publish the corrected immutable image, and deploy that
digest together with the application-GUID OAuth audience. Then verify protected
resource metadata, both authentication paths, and ChatGPT's interactive OAuth tool
discovery. These are deployment/operator actions, not remaining implementation
phases.

Phases 4–7 are implemented in source: hardened container and Aspire topology, S3-backed Terraform foundation/application, OIDC CI/release workflows, Route 53/custom TLS, Azure Monitor telemetry, operational scripts, threat model, and deployment/client documentation. The HTTP boundary supports Entra OAuth for ChatGPT alongside the original static API key, with unambiguous `Bearer` and `ApiKey` wire schemes.

## Locally verifiable evidence

- Unit, architecture, HTTP/stdio MCP contract, and non-live integration suites cover the nine-tool surface, API-key rejection, JWT signature/issuer/audience/lifetime validation, Entra protected-resource metadata, delegated-scope enforcement, ambiguous credential rejection, dual-auth initialization/tool discovery, bounded reads/search, cursor integrity, native-MOVE gating, Trash rejection, retry policy, and forbidden APIs.
- `Check-NoDelete.ps1` scans the product surface and dependencies for prohibited behavior.
- The Dockerfile is digest-pinned and runs as the chiseled non-root `app` user; Compose drops capabilities and uses a read-only filesystem.
- Terraform uses distinct encrypted S3 state keys in an operator-provided bucket, native lock files, managed identity, versionless Key Vault references, ACR admin disabled, one replica, Route 53 records, and a managed certificate binding.
- Actions use full commit SHAs and cloud OIDC; CI scans the image/configuration and produces an SBOM.
- Telemetry exports bounded tool/IMAP/rate-limit metrics and spans without MCP or mail payload values.

## External infrastructure

Deployment-specific resource names, identifiers, domains, and validation evidence are intentionally excluded from the public repository. Record them in a private deployment log.

## Requires external evidence

The following cannot be claimed until run with the relevant services and credentials:

- The opt-in Aspire closed-box test's SMTP seeding path; further investigation was stopped at the requested handoff because the project is otherwise working for the operator.
- Live Yahoo TLS/app-password status plus newest-ten read-only smoke test.
- S3 bucket versioning, encryption, and the complete least-privilege lock-object permission audit.
- Deployment of the corrected dual-auth image and application-GUID audience. The current production revision advertises protected-resource metadata in its 401 challenge but returns 404 from the advertised URL and uses the former resource-URL audience.
- ChatGPT's interactive OAuth authorization-code/refresh flow, tenant consent, and authenticated nine-tool discovery.
- Application Insights ingestion, dashboard/alert delivery, secret rotation drill, and rollback drill.

Use [Deployment](deployment.md) and [Operations](operations.md) for the exact commands. Record command output, commit SHA, image digest, Terraform plan hashes, cloud revision, and date without credentials or mailbox content.
