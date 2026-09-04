# Threat model

## Assets and trust boundaries

Protected assets are the Yahoo app password and mailbox data, MCP API key, OAuth tokens/client secret, cursor signing key, CloudFront origin-verification value, Azure/AWS identities, Key Vault, Terraform state, and telemetry. Trust boundaries exist at Route 53, CloudFront/WAF, the public HTTPS endpoint, MCP client, Container Apps ingress and origin verification, Entra authorization, Key Vault reference resolution, ACR pull, Yahoo TLS/IMAP connection, GitHub OIDC federation, and AWS infrastructure APIs.

## Principal threats and controls

| Threat | Control | Residual risk |
|---|---|---|
| Unauthorized mailbox access | HTTPS, independent bearer auth, constant-time token comparison, host filtering, rate limiting | A stolen client token grants the configured tool surface until rotated. |
| Credential disclosure | Key Vault references, OIDC, no secret Terraform inputs, redacted telemetry, ignored local files | Process memory necessarily contains credentials while active. |
| Mail-content disclosure through logs | No raw payload/protocol logs; allow-listed diagnostic metadata only | A compromised process can observe in-memory responses. |
| Destructive or emulated move | No delete tools/APIs; source scan; destination policy; native `MOVE` capability gate | A valid move changes mailbox organization and needs client approval. |
| Prompt injection in mail | Agent policy treats messages as untrusted data and requires approval for mutations | A client may still make poor decisions if it ignores policy. |
| Replay/tampering of cursors | HMAC-signed, bounded-age cursor with folder/UIDVALIDITY binding | Cursor rotation invalidates active pagination. |
| Denial of service | Fixed request limit, bounded page/body sizes, timeouts, single serialized IMAP session | Single-user service can be temporarily unavailable under abuse or Yahoo outage. |
| Supply-chain compromise | Locked NuGet restore, SHA-pinned Actions, digest-pinned images, scans and SBOM | Newly disclosed vulnerabilities require ongoing updates. |
| Cloud privilege escalation | Managed identity with ACR pull/Key Vault read only; protected environments and OIDC | Terraform deployment principals remain powerful and require governance. |
| State corruption/concurrency | Existing encrypted/versioned S3 bucket, distinct keys, native lock files | Bucket policy/versioning are external prerequisites and must be audited. |

## Review outcome

The V1 design keeps mailbox mutation narrow and explicit. The most significant residual risks are bearer-token theft, prompt injection influencing permitted mutations, and compromise of the runtime/deployment principal. Production approval should verify the external S3 bucket policy/versioning/encryption, GitHub OIDC subject restrictions, Route 53 role scope, Azure role assignments, alert delivery, and a completed secret-rotation drill.
