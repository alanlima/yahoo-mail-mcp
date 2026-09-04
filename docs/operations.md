# Operations

## Health and telemetry

`/health/live` checks only the process. `/health/ready` validates startup configuration and cached service readiness without authenticating to Yahoo on every probe. Both responses are generic.

Local telemetry flows to the Aspire Dashboard. Production telemetry flows to Application Insights when `APPLICATIONINSIGHTS_CONNECTION_STRING` is present. The service emits:

```text
yahoo_mail_mcp_tool_calls_total
yahoo_mail_mcp_tool_duration_ms
yahoo_mail_mcp_imap_operations_total
yahoo_mail_mcp_imap_operation_duration_ms
yahoo_mail_mcp_reconnects_total
yahoo_mail_mcp_auth_failures_total
yahoo_mail_mcp_rate_limit_rejections_total
yahoo_mail_mcp_active_requests
```

Dimensions are bounded safe names such as tool, operation, outcome, error code, retry count, and reconnect reason. No subject, body, address, search, folder name, attachment content, cursor, or credential is recorded.

Create an Azure Dashboard/Workbook showing request rate, 5xx rate, running replicas, tool failures, IMAP duration p95, reconnects, authentication failures, and rate-limit rejections. Terraform provisions alerts for zero replicas and repeated 5xx responses; add environment-specific notification thresholds for custom Application Insights metrics after their production baselines are known.

When CloudFront is enabled, use its standard request, 4xx, 5xx, and origin-error metrics plus WAF Web ACL/rule metrics for allowed, blocked, and rate-limited requests. WAF request sampling and detailed request logs are disabled to keep authorization values and private query data out of telemetry. Investigate scanner traffic through aggregate counts, not captured request payloads.

## Routine checks

- Review Container App health and revision status.
- Confirm exactly one active/running replica.
- Review 401/429 rates and tool/IMAP error outcomes without inspecting payloads.
- Check certificate expiry/status and Route 53 resolution.
- Confirm the CloudFront distribution is deployed, WAF remains associated, the strict allowlist blocks a harmless scanner path, and the Azure origin rejects a non-probe request without the verification header.
- Review ACR/Trivy findings and update pinned base-image digests deliberately.
- Run the local validation suite and the deployment discovery smoke test after each release.

## Secret rotation

Generate four independent replacement values in a secure password manager. Export them as `YAHOO_EMAIL`, `YAHOO_APP_PASSWORD`, `MCP_BEARER_TOKEN`, and `CURSOR_SIGNING_KEY`, then run:

```powershell
./scripts/Invoke-SecretRotationDrill.ps1 `
  -VaultName '<vault>' `
  -ResourceGroup '<resource-group>' `
  -ContainerAppName '<container-app>' `
  -BaseUri 'https://mcp.example.com'
```

The script writes new Key Vault versions, restarts the active revision to force refresh, and runs the authenticated deployment test. Rotating the cursor key invalidates existing cursors. Rotating the MCP token disconnects clients until they receive the new value. Revoke the previous Yahoo app password after the new one succeeds. Record only date, operator, secret names, revision, and outcome.

The ChatGPT OAuth client credential has a separate 180-day lifetime. Before expiry, add a replacement password credential to Entra, write its value as a new `mcp-oauth-client-secret` Key Vault version, update the ChatGPT connector credential, verify a fresh authorization flow, and only then remove the previous credential. The MCP runtime does not consume this secret, so restarting the Container App is unnecessary for this rotation.

Rotate CloudFront origin verification during a planned maintenance window. Update the Key Vault value and Terraform's sensitive CloudFront input together, apply, wait for both Azure and CloudFront to finish, and verify public traffic before clearing the shell value. A brief 403 window is possible while the two services converge. The exact sequence is in [CloudFront and AWS WAF](cloudfront-waf.md).

## Incident response

For suspected credential exposure, revoke the Yahoo app password, rotate MCP and cursor tokens, restart the revision, and inspect safe authentication/operation telemetry. Review repository history, workflow logs, Terraform state versions, artifacts, and image history for exposure.

For unexpected mutation, disable ingress or scale the Container App to zero, revoke the MCP token, preserve telemetry and deployment metadata, and rerun the no-delete checks before restoration. Never introduce delete/expunge behavior as remediation.

For degraded Yahoo connectivity, distinguish authentication failures from transient reconnects. Confirm Yahoo IMAP availability and app-password validity. Read calls can retry one transient disconnect; ambiguous mutations are intentionally never retried.

## Live smoke test

Live validation is opt-in and read-only. It requests account status and the newest ten INBOX summaries, writes no message data to test output, and never runs in CI:

```powershell
$env:YAHOO__EMAIL = '<address>'
$env:YAHOO__APPPASSWORD = '<app password>'
./scripts/Invoke-LiveSmokeTest.ps1
```
