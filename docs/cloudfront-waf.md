# CloudFront and AWS WAF

## Architecture and purpose

```text
Internet
   |
Route 53
   |
CloudFront
   |
AWS WAF
   |
HTTPS + X-Origin-Verify
   |
Azure Container Apps
   |
YahooMailMcp
```

Route 53 remains the authoritative DNS provider and the application remains on Azure Container Apps. CloudFront and AWS WAF reject generic scanner traffic before it reaches Azure. The CloudFront origin is the Container App's generated `azurecontainerapps.io` FQDN, never the configured public MCP hostname, so the DNS cutover cannot create an origin loop.

CloudFront redirects viewers to HTTPS, uses TLS 1.2 or newer to Azure, disables caching with AWS's managed `CachingDisabled` policy, and forwards all viewer headers except `Host` with the managed `AllViewerExceptHostHeader` policy. This preserves `Authorization`, `Content-Type`, `Accept`, `MCP-Protocol-Version`, `Mcp-Session-Id`, `Last-Event-ID`, query strings, cookies, and request bodies without caching authentication-sensitive responses. The origin receives its own Azure hostname in `Host`, which preserves the Container Apps TLS handshake.

## Public WAF surface

The repository's stateless MCP server disables the standalone GET stream and does not configure browser CORS. The edge allowlist therefore permits only:

| Priority | Method and path | Purpose |
| --- | --- | --- |
| 90 | `POST /mcp` | Authenticated stateless Streamable HTTP |
| 90 | `GET /.well-known/oauth-protected-resource` | RFC 9728 MCP OAuth discovery |
| 90 | `GET /` | Existing minimal service status |

The service does not host OAuth authorization-server or OpenID Provider metadata. Its protected-resource document points clients to the tenant-specific Entra issuer, and clients retrieve `oauth-authorization-server` or `openid-configuration` from Entra directly. The illustrative `/mcp/.well-known/*` routes are not mapped by this application and remain blocked.

CloudFront's behavior accepts the full method set because CloudFront has no `GET`, `HEAD`, `POST`-only behavior option. AWS WAF performs the exact method/path enforcement. `/health/live` and `/health/ready` are absent from the WAF allowlist; Azure probes call the origin directly and the application explicitly exempts only those two paths from origin verification.

## WAF evaluation

The Web ACL has a `BLOCK` default action. A terminating allow does not run until after all security checks:

| Priority | Rule | Normal action |
| --- | --- | --- |
| 20 | `AWSManagedRulesAmazonIpReputationList` | Managed action |
| 30 | `AWSManagedRulesKnownBadInputsRuleSet` | Managed action |
| 40 | `AWSManagedRulesCommonRuleSet` | Managed action |
| 50 | Per-IP rate limit for `POST /mcp` | Block above 100 requests per five minutes |
| 90 | Exact public method/path allowlist | Allow |
| default | Everything else | Block with generic 403 |

`SizeRestrictions_BODY` in the common rule set is overridden to `Count`. MCP JSON bodies can legitimately exceed the managed rule's small inspection threshold, and blocking solely on that size would create false positives. WAF still inspects the available body prefix with the other managed protections. SQL database and PHP/WordPress-specific groups are omitted because the service has no such runtime and the strict allowlist already blocks those paths. Bot Control is omitted because it adds cost and is unnecessary for a personal authenticated endpoint.

`waf_rate_limit` tunes the five-minute per-source-IP threshold. Keep enough headroom for client initialization and tool bursts. Set `waf_managed_rules_count_mode=true` temporarily to make all three managed groups non-terminating while diagnosing a false positive; the strict path allowlist and rate rule continue enforcing. Revert the setting promptly.

WAF and CloudFront standard CloudWatch metrics provide allowed, blocked, rate-limited, 4xx, 5xx, and origin-error visibility. Sampled WAF requests and request logs are deliberately disabled because they can retain authorization headers, query strings, or other private request data. Enable detailed logs only after designing redaction, retention, and cost controls.

## Origin-bypass protection

CloudFront overwrites `X-Origin-Verify` on origin requests. In production, middleware checks the configured value before MCP authentication using a constant-time comparison, removes the header before downstream diagnostics, and returns an empty generic 403 for missing or incorrect values. Local and test configurations keep `OriginProtection:Enabled=false`.

The current value is stored as `origin-verification-header` in Azure Key Vault and exposed to Container Apps through a versionless secret reference. Use at least 32 random bytes. Supply the same value to Terraform only through `TF_VAR_origin_verification_header_value`; never put it in source or a committed tfvars file.

CloudFront's API requires the custom-header value in the distribution configuration. Consequently, Terraform must retain this one value in the encrypted application state even though the variable and all plan expressions are sensitive. Restrict S3 state access, keep bucket encryption and versioning enabled, and treat state readers as secret readers. The value is never output by Terraform.

For rotation, schedule a maintenance window, write the new value to `origin-verification-header`, supply the same value through `TF_VAR_origin_verification_header_value`, and apply the Azure and CloudFront changes together. Wait for the Container App revision and CloudFront distribution to finish deploying, verify public traffic, disable the old Key Vault version, and clear the shell variables. Because only one value is accepted, a brief 403 window is possible while Azure and CloudFront converge.

Never log either value. CloudFront origin verification is defence in depth; `POST /mcp` still requires either `Authorization: ApiKey <key>` or `Authorization: Bearer <Entra JWT>`.

## Staged deployment and rollback

No CloudFront resources are created by default. Use the low DNS TTL and perform reviewed applies in this order:

1. Generate the origin value, store it in Key Vault, and export the same value as `TF_VAR_origin_verification_header_value`.
2. Set `cloudfront_enabled=true`, while leaving `origin_protection_enabled=false` and `cloudfront_route53_cutover=false`. Apply to create ACM DNS validation, the Web ACL, and CloudFront without changing public DNS.
3. Read `cloudfront_distribution_domain_name`. Test `POST /mcp` through `https://<distribution>.cloudfront.net`; use the direct `health_url` output for probe checks. Scanner paths should return 403 at the edge.
4. After CloudFront is `Deployed`, set both `origin_protection_enabled=true` and `cloudfront_route53_cutover=true`, review the plan, and apply. Terraform changes only the existing MCP record from the Azure CNAME to CloudFront A/AAAA aliases and creates a protected Container App revision.
5. Wait for the configured DNS TTL plus CloudFront propagation, then test API-key authentication, Entra authentication, OAuth protected-resource discovery, and blocked scanner paths through the configured MCP hostname.

The existing Azure custom-domain and managed-certificate resources are intentionally retained. To roll back, set `cloudfront_route53_cutover=false` and `origin_protection_enabled=false`, apply, wait for DNS caches, and verify the Azure path. Do not remove CloudFront/WAF/ACM until rollback traffic is healthy.

## CloudFront flat-rate Free plan and cost

AWS currently publishes a CloudFront Free flat-rate plan with 1 million requests and 100 GB per month, including CloudFront, AWS WAF, standard CloudWatch log ingestion, and eligible Route 53 usage. The repository-pinned AWS provider does not yet expose the Pricing Plan Manager subscription resource, so Terraform does not use CLI, `local-exec`, or an unsupported workaround.

After deployment, inspect the distribution in the AWS CloudFront console and attach the Free plan manually if the account and this dedicated distribution/Web ACL are eligible. The Free plan permits up to five WAF rules; this Web ACL uses exactly five top-level rules when all managed groups are enabled. If AWS evaluates managed-group internals against the allowance or rejects another feature, leave the distribution on pay-as-you-go rather than weakening protection. An AWS Free Tier account cannot use flat-rate plans.

Without an attached flat-rate plan, CloudFront, WAF Web ACL/rules/requests, Route 53 queries, ACM public certificates used by CloudFront, and optional logging follow their normal AWS pricing. Detailed WAF/CloudFront logging remains off to avoid recurring storage and query cost. Review the current official [CloudFront flat-rate plan documentation](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/flat-rate-pricing-plan.html) and AWS pricing calculator before applying.

## IAM permissions

In addition to the existing S3 state and Route 53 permissions, the application Terraform role needs narrowly scoped permissions for:

- CloudFront distribution create, read, update, delete, tag operations, and managed cache/origin-request policy reads.
- WAFv2 CloudFront-scope Web ACL create, read, update, delete, tag operations, and managed-rule-group discovery in `us-east-1`.
- ACM request, describe, validate, delete, and tag operations in `us-east-1`.
- Route 53 hosted-zone reads, record changes, record listing, and change status reads for the existing hosted zone.

Pricing Plan Manager permissions are not required by Terraform. Grant them only to the operator who performs the optional console subscription. Do not use `AdministratorAccess`.
