# Security

This is a single-user mailbox service. Its highest-priority invariant is that it cannot delete, trash, purge, expunge, send, draft, reply, forward, execute raw IMAP, or download attachment content. A normal move is allowed only when the authenticated server advertises native IMAP `MOVE` and the destination is neither Trash nor a configured alias.

Remote MCP requires either a separate 32-byte-or-longer API key or a validated Entra OAuth access token, HTTPS-only production ingress, application and edge rate limiting, and one Container Apps replica. Yahoo credentials authenticate only the server to IMAP; they do not authorize MCP clients. When enabled, CloudFront/WAF adds a default-deny path boundary and the Azure origin rejects non-probe requests without CloudFront's private verification header before MCP authentication.

Secrets are accepted through local user-secrets/environment variables or versionless Key Vault references. They are excluded from repository files, committed tfvars, Docker layers, logs, traces, snapshots, and CI artifacts. ACR admin credentials are disabled and the runtime uses a user-assigned managed identity for ACR pull and Key Vault secret read. CloudFront's custom-origin API is the one documented state exception: its verification value is marked sensitive but necessarily retained in encrypted, access-controlled Terraform state; it is never output.

Telemetry deliberately excludes full MCP request/response payloads because those objects contain mail subjects, bodies, addresses, search terms, folder names, and identifiers. Development diagnostics log only allow-listed method/tool names and argument field names. MailKit protocol logging is disabled.

Before release, run the source no-delete scan, architecture tests, full tests, formatter, container scan, Terraform format/validate/security scan, SBOM generation, and authenticated deployment discovery. Live Yahoo and cloud deployment require explicit manual authorization and credentials.

Report vulnerabilities privately. Never paste credentials or mailbox content into an issue, chat, log excerpt, or support ticket.
