# Deployment

Production runs one Azure Container Apps replica, stores secrets in Azure Key Vault, publishes images to ACR, exports telemetry to Application Insights, and manages the public record in an existing AWS Route 53 zone. Terraform state remains in the existing S3 bucket `lima-terraform-states`; this repository does not create or modify that bucket.

## Current state and next action

The source implementation supports API-key and Entra OAuth authentication on the
same `/mcp` endpoint. The Entra registration, delegated scope, Key Vault values,
Route 53 records, custom hostname, and Azure-managed certificate are provisioned.

The next release must deploy the corrected container image and the application-GUID
OAuth audience together. Do not apply only the audience change to the older image.
The release sequence is: build and push an immutable image, place its digest in the
application variables, confirm `mcp_oauth_audience` is the Entra application GUID,
plan the combined Container App revision change, apply it, and run the checks in
section 7. ChatGPT connection is the final interactive verification.

## Required access

- Terraform 1.14+, Azure CLI, AWS CLI, Docker, and PowerShell 7.
- Azure Contributor (or a narrower resource-creation role) plus permission to create role assignments for the foundation deployment.
- Microsoft Graph permission to create an Entra app registration and its service principal, or an administrator who can run the provided provisioning script.
- AWS access to the Route 53 public zone and state objects, plus scoped CloudFront, WAFv2, and `us-east-1` ACM permissions when the edge layer is enabled.
- The state principal needs `s3:ListBucket` on `lima-terraform-states` for the `yahoo-mail-mcp/prod/` prefix and `s3:GetObject`, `s3:PutObject`, and `s3:DeleteObject` on both state and `.tflock` objects in that prefix. Bucket versioning and server-side encryption should already be enabled.
- A Yahoo app password. Never use the normal Yahoo account password.

The two state objects are:

```text
s3://lima-terraform-states/yahoo-mail-mcp/prod/foundation.tfstate
s3://lima-terraform-states/yahoo-mail-mcp/prod/application.tfstate
```

Native S3 lock files are enabled with `use_lockfile=true`; no DynamoDB lock table is required.

## 1. Configure local inputs

Copy the examples to ignored files and replace placeholders:

```powershell
Copy-Item ./infra/terraform/environments/prod/foundation.tfvars.example ./infra/terraform/environments/prod/foundation.tfvars
Copy-Item ./infra/terraform/environments/prod/application.tfvars.example ./infra/terraform/environments/prod/application.tfvars
```

Confirm the S3 bucket region. If it is not `ap-southeast-2`, update `aws_region` and the backend `region` commands below. The backend key is supplied per stack and must never be shared between them.

## 2. Deploy foundation

Authenticate with Azure and AWS, then:

```powershell
terraform -chdir=infra/terraform/foundation init `
  -backend-config="bucket=lima-terraform-states" `
  -backend-config="key=yahoo-mail-mcp/prod/foundation.tfstate" `
  -backend-config="region=ap-southeast-2" `
  -backend-config="encrypt=true" `
  -backend-config="use_lockfile=true"
terraform -chdir=infra/terraform/foundation fmt -check -recursive
terraform -chdir=infra/terraform/foundation validate
terraform -chdir=infra/terraform/foundation plan -var-file=../environments/prod/foundation.tfvars -out=foundation.tfplan
terraform -chdir=infra/terraform/foundation apply foundation.tfplan
```

Foundation creates the resource group, ACR with admin credentials disabled, Log Analytics, Application Insights, Container Apps environment, user-assigned identity, Key Vault, least-required runtime role assignments, action group, and CAF-derived names.

## 3. Provision secrets outside Terraform

Set values only in the current secure shell or CI secret store. The two random keys must contain at least 32 UTF-8 bytes.

```powershell
$env:YAHOO_EMAIL = 'address@yahoo.com'
$env:YAHOO_APP_PASSWORD = '<Yahoo app password>'
$env:MCP_BEARER_TOKEN = '<32+ random bytes>'
$env:CURSOR_SIGNING_KEY = '<different 32+ random bytes>'
./scripts/Set-KeyVaultSecrets.ps1 -VaultName '<foundation key_vault_name output>'
```

The script clears these four process environment variables when it finishes. Terraform contains only versionless Key Vault references and never receives their values.

Before enabling CloudFront, generate a separate 32-byte-or-longer random value, keep it in the secure shell, and provision it with the runtime secrets:

```powershell
$env:ORIGIN_VERIFICATION_HEADER_VALUE = [Convert]::ToBase64String(
  [Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$env:TF_VAR_origin_verification_header_value = $env:ORIGIN_VERIFICATION_HEADER_VALUE
./scripts/Set-KeyVaultSecrets.ps1 `
  -VaultName '<foundation key_vault_name output>' `
  -IncludeOriginProtection
```

`Set-KeyVaultSecrets.ps1` requires the four normal runtime environment variables in the same shell and clears all values it provisions. Re-export only `TF_VAR_origin_verification_header_value` from the password manager before an edge Terraform plan. CloudFront requires this value in its API configuration, so it is retained as sensitive data in the encrypted application state; it is never a Terraform output or committed input.

## 4. Provision the ChatGPT OAuth app

Run once after the Key Vault exists:

```powershell
./scripts/New-EntraMcpApplication.ps1 `
  -ResourceUri 'https://mcp.alanlima.cloud/mcp' `
  -ChatGptRedirectUri 'https://chatgpt.com/connector_platform_oauth_redirect' `
  -VaultName 'kv-ymcp-prod-aue-01'
```

The script creates a single-tenant Entra app, exposes the `access_as_user` delegated scope, creates its service principal and a 180-day client credential, and writes only `mcp-oauth-client-id`, `mcp-oauth-client-secret`, and `mcp-oauth-tenant-id` to Key Vault. It does not print the client secret. The current provisioned client ID is `7553b3c9-f602-42a8-b5fe-8a54af0dd3af`.

Set `mcp_oauth_enabled=true`, the tenant GUID, and `mcp_oauth_audience="7553b3c9-f602-42a8-b5fe-8a54af0dd3af"` in the ignored production application variables. The audience is the application GUID; the full resource URL remains the requested OAuth scope prefix. The Container App reads the client ID through a Key Vault reference; it intentionally does not load the OAuth client secret.

OAuth may also be enabled in Development for debugging. The checked-in Development
settings contain only non-secret identifiers; local secrets remain in user-secrets
or environment variables.

## 5. Publish an immutable image

Use ACR login through Azure identity, then capture the pushed digest:

```powershell
az acr login --name '<foundation container_registry_name output>'
docker build -f ./src/YahooMailMcp.Server.Http/Dockerfile -t '<registry>.azurecr.io/yahoo-mail-mcp:release' .
docker push '<registry>.azurecr.io/yahoo-mail-mcp:release'
docker inspect --format='{{index .RepoDigests 0}}' '<registry>.azurecr.io/yahoo-mail-mcp:release'
```

Use only the `sha256:...` portion as `image_digest`. CI performs the same build plus vulnerability scan and SBOM generation.

## 6. Deploy application and DNS

```powershell
terraform -chdir=infra/terraform/application init `
  -backend-config="bucket=lima-terraform-states" `
  -backend-config="key=yahoo-mail-mcp/prod/application.tfstate" `
  -backend-config="region=ap-southeast-2" `
  -backend-config="encrypt=true" `
  -backend-config="use_lockfile=true"
terraform -chdir=infra/terraform/application fmt -check -recursive
terraform -chdir=infra/terraform/application validate
terraform -chdir=infra/terraform/application plan -var-file=../environments/prod/application.tfvars -out=application.tfplan
terraform -chdir=infra/terraform/application apply application.tfplan
```

The application stack creates the one-replica Container App, versionless Key Vault references, Route 53 and `asuid` verification records, managed certificate binding, and availability/HTTP alerts. With `cloudfront_enabled=true`, it also creates a `us-east-1` ACM certificate, a default-deny WAF Web ACL, and a CloudFront distribution against the generated Azure hostname.

Stage the edge migration. First leave `origin_protection_enabled=false` and `cloudfront_route53_cutover=false`, apply, and validate the `cloudfront_distribution_domain_name` output. Then enable origin protection and the Route 53 cutover together in a reviewed plan. The final record is a CloudFront A/AAAA alias; the prior Azure CNAME remains the rollback configuration. Health checks use the direct `health_url` output because WAF intentionally does not expose probe routes. See [CloudFront and AWS WAF](cloudfront-waf.md) for the exact test, rollback, rotation, and optional Free-plan steps.

The application still registers the Azure custom hostname first, requests the Azure-managed certificate second, and binds the certificate last. This ordering avoids Azure's `RequireCustomHostnameInEnvironment` error. Those resources are retained after cutover for safe rollback. DNS, ACM validation, Azure certificate issuance, and CloudFront propagation can take time.

The wildcard certificate in AWS Certificate Manager cannot be referenced directly by Azure Container Apps. Azure would require an exportable certificate plus its private key uploaded as a PFX. Because the current design uses an Azure-managed certificate after Route 53 validation, ACM certificate `87b6aa77-0fb3-4af3-a77d-62a90d570a83` is not copied or exposed.

## 7. Verify

```powershell
$env:MCP_BEARER_TOKEN = '<current MCP token>'
$healthBaseUri = terraform -chdir=infra/terraform/application output -raw health_url
./scripts/Test-Deployment.ps1 -BaseUri 'https://mcp.example.com' -HealthBaseUri $healthBaseUri -ApiKey $env:MCP_BEARER_TOKEN -ExpectOAuth
Remove-Item Env:MCP_BEARER_TOKEN
```

This checks liveness/readiness, authenticated initialization, and the exact nine-tool surface. Also confirm an unauthenticated POST to `/mcp` returns 401 and inspect Application Insights for content-safe traces and metrics.

Before configuring ChatGPT, also verify the OAuth discovery boundary:

```powershell
Invoke-WebRequest 'https://mcp.alanlima.cloud/.well-known/oauth-protected-resource'
```

The response must identify `https://mcp.alanlima.cloud/mcp` as the resource, the tenant-specific Entra v2 authorization server, and the qualified `access_as_user` scope. The final interactive authorization-code flow can be verified only from ChatGPT.

Also verify both authentication paths after the same deployment:

```text
Authorization: ApiKey <static MCP key>
Authorization: Bearer <Entra access token with access_as_user>
```

An invalid Bearer token, an API key sent with the Bearer scheme, multiple
Authorization values, or an unknown scheme must return 401. A valid JWT without
`access_as_user` must return 403.

## GitHub OIDC setup

Create protected `development` and `production` environments with approval on production. Configure Azure federated credentials and an AWS GitHub OIDC role; do not create long-lived cloud keys. Required repository/environment variables are:

```text
AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID
AZURE_CONTAINER_REGISTRY_NAME, AZURE_KEY_VAULT_NAME
MCP_OAUTH_CLIENT_ID
AWS_ROLE_TO_ASSUME, AWS_REGION
TF_GLOBAL_SUFFIX, RESOURCE_OWNER, ALERT_EMAIL
MCP_FQDN, ROUTE53_ZONE_NAME
CLOUDFRONT_ENABLED, CLOUDFRONT_ROUTE53_CUTOVER
ORIGIN_PROTECTION_ENABLED
```

Store `ORIGIN_VERIFICATION_HEADER_VALUE` as a protected GitHub environment secret in both the `development` plan environment and `production` apply environment, never as a variable. The workflow passes it to Terraform as a sensitive input. It uploads only redacted text plan summaries; binary Terraform plans are deliberately not artifacts because they contain sensitive values. After production-environment approval, the apply job creates a fresh ephemeral plan and applies it in the same job. Keep the three enable/cutover variables false until the staged deployment reaches the corresponding step.

The AWS role needs the state permissions above for both stacks and Route 53 read/change permissions for the application stack. When CloudFront is enabled, add narrowly scoped CloudFront distribution, WAFv2 CloudFront-scope Web ACL, and `us-east-1` ACM certificate permissions described in [CloudFront and AWS WAF](cloudfront-waf.md). Run the Infrastructure workflow for `foundation` first, provision secrets, then run it for `application`. The Release workflow builds, scans, publishes, deploys an immutable digest, and runs MCP discovery.

## Rollback

Find the last known-good digest and run an application plan/apply with that digest. Container Apps is configured for single-revision traffic, so Terraform creates and activates the replacement revision. Do not automatically roll back Key Vault versions unless a secret rotation is the confirmed cause. After rollback, run `Test-Deployment.ps1` and record the digest, revision, Terraform commit, and result.

Never use Aspire publish for production; Terraform is the reviewed deployment authority.
