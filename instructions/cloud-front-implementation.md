Implement AWS CloudFront + AWS WAF in front of the existing YahooMailMcp Azure Container Apps deployment.

## Goal

The current public MCP endpoint is:

`https://mcp.alanlima.cloud`

DNS is hosted in AWS Route 53, while the application itself runs in Azure Container Apps.

The endpoint is receiving generic internet vulnerability scans such as:

- `/.env`
- `/server.key`
- `/backup.zip`
- `/dump.sql`
- `/database.sql`
- `/wp-admin/*`
- `/wp-includes/*`
- `/phpinfo.php`
- `/config.php`
- `/actuator/heapdump`
- `/storage/logs/laravel.log`

The application is currently returning 404/405 correctly, but I want this traffic rejected at the edge before it reaches Azure.

Implement:

`Route 53 -> CloudFront -> AWS WAF -> Azure Container Apps -> YahooMailMcp`

Use Terraform and integrate with the existing infrastructure structure rather than creating manual resources.

## Important constraints

1. Preserve the current Azure Container Apps deployment.
2. Keep Route 53 as the DNS provider.
3. Do not migrate the application into AWS.
4. Infrastructure must remain fully Terraform-managed.
5. Do not hard-code secrets in Terraform source, tfvars committed to Git, GitHub Actions, or application settings.
6. Keep the current MCP authentication behavior:
   - API-key bearer token
   - Entra/OAuth JWT
7. Do not interfere with valid MCP OAuth discovery endpoints.
8. Keep implementation economical and appropriate for a personal project.
9. Prefer the AWS CloudFront Free flat-rate plan if Terraform/API support permits it cleanly.
10. Do not perform deployment against my real environments. Implement, validate Terraform syntax/tests/docs, but leave actual `terraform apply` and container deployment to me.

## Existing public surface

The MCP service intentionally exposes only a very small HTTP surface.

At minimum preserve these routes:

- `/mcp`
- `/.well-known/oauth-protected-resource`
- `/.well-known/oauth-authorization-server`
- `/.well-known/openid-configuration`
- `/mcp/.well-known/oauth-protected-resource`
- `/mcp/.well-known/oauth-authorization-server`
- `/mcp/.well-known/openid-configuration`

Also preserve the existing health endpoints required by Azure Container Apps probes.

Do not expose health endpoints publicly through CloudFront unless required.

Review the code before implementing the final allowlist because the repository is the source of truth for actual MCP, OAuth and health routes.

## Terraform structure

First inspect the existing Terraform structure and follow its conventions.

Do not arbitrarily put everything into a single `main.tf`.

Prefer logical modules/files consistent with the repository, for example:

```text
infra/
  ...
  aws/
    cloudfront.tf
    waf.tf
    route53.tf
    acm.tf
    variables.tf
    outputs.tf
```

or corresponding Terraform modules if the project already uses modules.

Reuse the existing AWS provider and Route 53 configuration if present.

If AWS resources currently live together with Azure infrastructure, follow the existing pattern instead of reorganising the whole repository.

Do not perform unrelated Terraform refactoring.

## AWS provider requirement

CloudFront WAFv2 resources with scope `CLOUDFRONT` must be created in `us-east-1`.

If the project AWS provider is configured for another region, introduce an aliased provider:

```hcl
provider "aws" {
  alias  = "us_east_1"
  region = "us-east-1"
}
```

Use that provider for:

- WAFv2 Web ACL
- ACM certificate used by CloudFront, if applicable

CloudFront itself is global.

## CloudFront distribution

Create a CloudFront distribution whose custom origin is the Azure Container Apps public FQDN.

Do NOT use `mcp.alanlima.cloud` itself as the origin because that would create a DNS loop after Route 53 points the hostname to CloudFront.

The origin must be the underlying Azure Container Apps hostname, for example conceptually:

`<container-app>.<environment>.<region>.azurecontainerapps.io`

Derive it from existing Terraform outputs/resources where possible.

Do not duplicate the hostname manually if Terraform can reference it.

Configure:

- HTTPS origin only
- TLS 1.2+
- Redirect HTTP viewers to HTTPS
- IPv6 enabled unless existing project requirements say otherwise
- compression where sensible
- caching disabled or effectively bypassed for MCP/API requests
- all required HTTP methods
- forward request bodies
- preserve required headers
- preserve query strings if the application uses them
- do not cache authentication-sensitive responses

### Authorization header

This is critical.

CloudFront must forward the viewer `Authorization` header to Azure because `/mcp` uses bearer authentication.

Ensure both of these continue to work:

```http
Authorization: Bearer <api-key>
```

and:

```http
Authorization: Bearer <entra-jwt>
```

Do not create a cache configuration that varies incorrectly or strips Authorization.

Prefer an appropriate CloudFront origin request policy/cache policy for a dynamic authenticated API.

## Origin-bypass protection

Prevent clients from bypassing CloudFront and calling the Azure Container Apps FQDN directly.

CloudFront supports adding a custom header to requests sent to a custom origin. Use this mechanism.

Generate/use a strong secret value such as:

`X-Origin-Verify`

Do not hard-code the secret in source control.

CloudFront sends:

```http
X-Origin-Verify: <secret>
```

Add ASP.NET middleware or equivalent application logic that verifies this header for public production traffic.

Requests reaching the Azure Container App without the correct header should be rejected before MCP authentication.

However, make this configurable so local development and existing tests do not break.

Suggested configuration:

```text
OriginProtection:Enabled
OriginProtection:HeaderName
OriginProtection:HeaderValue
```

or equivalent configuration following existing conventions.

Store the value through the project's existing secure secret-management mechanism.

If Azure Container Apps configuration is Terraform-managed, expose the secret to the container using an ACA secret/environment variable rather than plain Terraform source.

Do not log the secret.

Use a constant-time comparison where appropriate.

### Health probes

Azure Container Apps health probes originate directly from Azure and will not pass through CloudFront.

Therefore the origin-protection middleware MUST exclude the existing health probe endpoints.

For example:

```text
/health/live
/health/ready
```

Use the actual repository routes.

Health endpoints must remain minimal and must not expose sensitive information.

## AWS WAF

Create an AWS WAFv2 Web ACL:

```text
scope = CLOUDFRONT
```

and associate it with CloudFront using the CloudFront distribution's `web_acl_id`.

Do NOT use `aws_wafv2_web_acl_association` for CloudFront.

## WAF strategy

Use a combination of:

1. AWS managed rules
2. rate limiting
3. strict application-specific path/method rules

### Managed rule groups

Enable sensible AWS managed rule groups appropriate for an HTTP API.

At minimum evaluate:

- AWSManagedRulesCommonRuleSet
- AWSManagedRulesKnownBadInputsRuleSet
- AWSManagedRulesAmazonIpReputationList

Evaluate other groups before enabling them to avoid unnecessary false positives.

Do not blindly enable every AWS managed rule group.

Document any groups deliberately omitted and why.

## Application path allowlist

The application has an extremely small surface.

Prefer an allowlist rather than maintaining dozens of scanner-specific blacklist patterns.

Allow valid combinations such as:

```text
POST /mcp
GET /.well-known/*
GET /mcp/.well-known/*
```

and any other methods/routes confirmed from the application.

Be careful with MCP transport behavior: inspect the actual implementation and MCP SDK requirements before restricting `/mcp` to POST only.

If GET, DELETE, OPTIONS or other methods are legitimately required by the MCP Streamable HTTP transport, allow them.

The repository/application behavior is the source of truth.

Everything outside the intended public API surface should be blocked at WAF.

Return a generic status such as 403.

Do not reveal internal framework/application information.

The following should consequently never reach Azure:

```text
/.env
/.npmrc
/server.key
/backup.zip
/database.sql
/dump.sql
/wp-*
/phpinfo.php
/config.php
/actuator/*
/storage/*
```

Do not create individual rules for all of those if the allowlist already prevents them.

## OAuth discovery

Be especially careful not to block legitimate MCP authorization discovery.

These probes are valid candidates:

```text
/.well-known/oauth-protected-resource
/.well-known/oauth-authorization-server
/.well-known/openid-configuration

/mcp/.well-known/oauth-protected-resource
/mcp/.well-known/oauth-authorization-server
/mcp/.well-known/openid-configuration
```

Verify exactly which of these the service intentionally supports.

Do not mistake these requests for malicious scanning.

## Rate limiting

Add WAF rate limiting.

Because this is a personal MCP endpoint with very low expected traffic, start conservatively.

Suggested initial limit:

```text
100 requests / 5 minutes / source IP
```

or the closest supported AWS WAF rate model.

Apply rate limiting primarily to the MCP/public API surface.

Do not configure limits so tightly that ChatGPT's MCP initialization/discovery sequence is intermittently blocked.

Make the value configurable through Terraform.

Example:

```hcl
variable "waf_rate_limit" {
  type    = number
  default = 100
}
```

Document how to tune it.

## WAF rule priority

Use explicit predictable priorities.

Conceptually:

```text
10  - allow legitimate public MCP/OAuth routes
20  - AWS IP reputation
30  - known bad inputs
40  - common managed protections
50  - rate limit
100 - block everything outside expected surface
```

However, remember that WAF terminating `ALLOW` rules can prevent later rules from being evaluated.

Design the rule evaluation correctly.

It may be preferable for the Web ACL default action to be BLOCK while managed security/rate checks are applied to allowed paths using scope-down statements.

Do not create an allow rule early in evaluation that accidentally bypasses security inspection.

Document the chosen evaluation model.

## Route 53

Update the existing Route 53 record for:

`mcp.alanlima.cloud`

so that it points to CloudFront rather than directly to Azure Container Apps.

Prefer an Alias record if compatible with the existing Terraform setup.

Preserve the existing hosted zone.

Do not modify unrelated DNS records.

## TLS certificate

CloudFront custom domains require an ACM certificate in `us-east-1`.

Check whether the repository already contains a wildcard or appropriate ACM certificate for:

`*.alanlima.cloud`

or:

`mcp.alanlima.cloud`

Reuse an existing suitable certificate where appropriate.

Otherwise create an ACM certificate through Terraform and validate it through Route 53 DNS.

Certificate:

```text
mcp.alanlima.cloud
```

Do not replace or interfere with Azure's origin TLS certificate.

CloudFront-to-origin communication should continue using HTTPS to the Azure Container Apps hostname.

## CloudFront custom domain

Configure:

```text
aliases = ["mcp.alanlima.cloud"]
```

and use the ACM certificate.

Minimum TLS:

```text
TLSv1.2_2021
```

or the currently recommended secure CloudFront policy supported by the provider.

## Cache behaviour

This is an authenticated MCP API, not a static website.

Avoid caching API responses.

Use either the AWS managed caching-disabled policy or an equivalent Terraform-defined policy.

Ensure these are forwarded as required:

- Authorization
- Content-Type
- Accept
- MCP-specific headers
- query strings
- request body
- origin-related protocol headers as required by the MCP implementation

Review whether the MCP SDK uses headers such as:

```text
Mcp-Session-Id
Last-Event-ID
```

and make sure CloudFront forwards them.

Do not assume a conventional REST API header set.

## CORS / OPTIONS

Inspect the application for CORS configuration.

If OPTIONS is required, make sure:

- CloudFront allows OPTIONS
- WAF allows valid OPTIONS requests
- required access-control headers are forwarded

Do not loosen CORS unnecessarily.

## Terraform variables

Expose sensible configurable inputs, following current project naming conventions.

Candidates include:

```text
cloudfront_enabled
mcp_domain_name
origin_protection_enabled
origin_verification_header_name
waf_rate_limit
cloudfront_price_class
enable_waf_managed_rules
```

Do not expose secret values through normal committed tfvars.

## Terraform outputs

Add useful outputs such as:

```text
cloudfront_distribution_id
cloudfront_distribution_domain_name
waf_web_acl_arn
mcp_public_url
```

Do not output sensitive origin-verification values.

Mark anything sensitive appropriately if absolutely necessary.

## CloudFront pricing plan

AWS currently offers CloudFront flat-rate plans, including a Free tier intended for hobby/development use with a published allowance of 1 million requests and 100 GB/month.

Investigate whether the AWS provider currently supports attaching/configuring the CloudFront flat-rate Free plan declaratively.

If Terraform support is stable and appropriate, configure it.

If Terraform does NOT currently expose this cleanly:

- do not introduce local-exec scripts
- do not call AWS CLI from Terraform
- do not create brittle unsupported workarounds
- document the manual post-deployment step instead

The core CloudFront/WAF infrastructure must still remain Terraform-managed.

## Logging and observability

The previous reason for adding this layer is that Application Insights is currently recording generic vulnerability scanner traffic.

Enable enough AWS-side observability to diagnose blocked requests without creating unnecessary recurring cost.

Because the CloudFront Free flat-rate plan may have different logging capabilities than paid tiers, confirm the currently supported logging features.

At minimum provide metrics/visibility for:

- allowed requests
- blocked WAF requests
- rate-limited requests
- CloudFront 4xx
- CloudFront 5xx
- origin errors

Avoid enabling expensive logging blindly.

If full request logging has additional cost or is unavailable under Free, document that.

## Security

The following are mandatory:

- Azure origin only accepts CloudFront-originated production requests, except health probes
- origin verification secret is never committed
- `/mcp` authentication remains mandatory
- API key and JWT logic remain unchanged
- HTTPS enforced
- AWS WAF default posture is deny except intended public routes
- scanner traffic is rejected at AWS edge
- no internal stack traces returned
- no security-sensitive values emitted by Terraform outputs
- no origin secret appears in logs

CloudFront origin verification is defence-in-depth, not replacement authentication.

The effective flow must remain:

```text
Internet
    |
    v
Route 53
    |
    v
CloudFront
    |
    v
AWS WAF
    |
    | X-Origin-Verify
    v
Azure Container Apps
    |
    +--> origin verification
    |
    +--> MCP authentication
           |
           +--> API-key bearer
           |
           +--> Entra JWT
    |
    v
YahooMailMcp
```

## Tests

Add/update tests covering application changes.

At minimum:

1. correct origin header + valid MCP authentication -> reaches MCP
2. missing origin header -> rejected
3. incorrect origin header -> rejected
4. health endpoints work without origin header
5. local/dev mode works when origin protection is disabled
6. origin header value is never logged
7. existing API-key authentication tests still pass
8. existing JWT authentication tests still pass

For Terraform:

- `terraform fmt`
- `terraform validate`
- existing Terraform static-analysis/tests if present

If the repository uses TFLint, Checkov, Terratest or equivalent, update/run those as appropriate.

Do NOT perform a production `terraform apply`.

## GitHub Actions

Review existing Terraform/deployment workflows.

Update them only if necessary for the additional AWS resources.

Do not break Azure deployments.

If AWS credentials are already configured for Route 53 Terraform operations, reuse the existing authentication mechanism.

Prefer GitHub OIDC / IAM role assumption over long-lived AWS keys where the project already supports it.

If IAM permissions need expanding, document the minimum required AWS permissions for:

- CloudFront
- WAFv2
- ACM
- Route 53
- CloudWatch where required

Do not grant broad AdministratorAccess just to make deployment easier.

## Documentation

Update the project architecture documentation and README.

Include:

### Architecture

```text
Internet
   |
Route 53
   |
CloudFront
   |
AWS WAF
   |
HTTPS
   |
Azure Container Apps
   |
YahooMailMcp
```

### Explain

- why CloudFront/WAF was introduced
- why Route 53 remains in AWS
- why the application remains in Azure
- how origin bypass prevention works
- which routes are publicly permitted
- how rate limiting works
- how OAuth discovery is handled
- where the origin verification secret lives
- how to rotate the origin verification secret
- CloudFront Free-plan considerations
- expected incremental infrastructure cost
- troubleshooting WAF false positives
- how to temporarily use WAF Count mode for diagnosis

## Deployment safety

CloudFront propagation and DNS migration can make this change disruptive.

Structure the Terraform/deployment so the order is:

1. provision ACM
2. provision WAF
3. provision CloudFront against the existing Azure origin
4. validate CloudFront using its generated `*.cloudfront.net` domain
5. configure Azure origin verification
6. ensure CloudFront requests succeed
7. change Route 53 `mcp.alanlima.cloud`
8. verify MCP + OAuth authentication through the public hostname
9. only then remove obsolete infrastructure/configuration

Where Terraform dependencies are required, express them through references rather than arbitrary `depends_on` unless necessary.

Do not destroy the existing Container App or its custom-domain configuration until it is proven unnecessary.

## Important implementation detail

Do not assume that the Container Apps direct hostname can simply be disabled because CloudFront still needs a reachable custom origin.

The application-level CloudFront verification header is therefore the initial bypass-prevention mechanism.

If there is a stronger Azure Container Apps ingress restriction that can reliably permit all CloudFront traffic without maintaining unstable IP ranges, investigate and document it, but do not replace the custom origin-verification mechanism unless it provides equivalent or stronger protection.

## Deliverables

Make the actual repository changes for:

- Terraform
- application origin-verification middleware
- configuration
- tests
- GitHub Actions if needed
- README
- architecture/security documentation

Then provide a concise implementation report containing:

1. files changed
2. Terraform resources added
3. WAF rules and priorities
4. routes allowed publicly
5. origin-bypass design
6. secret/configuration requirements
7. IAM changes required
8. manual actions still required from me
9. tests run and results
10. any assumptions or unresolved limitations

Do not deploy the infrastructure yourself.

Do not run the application in Docker unless required for a lightweight automated test. Leave actual container build/deployment and Terraform apply to me.
