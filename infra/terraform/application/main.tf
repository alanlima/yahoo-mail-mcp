locals {
  base           = "${var.workload}-${var.environment}-${var.region_code}-${var.instance}"
  container_name = "ca-${local.base}"
  image          = "${data.terraform_remote_state.foundation.outputs.container_registry_login_server}/${var.image_repository}@${var.image_digest}"
  vault_uri      = data.terraform_remote_state.foundation.outputs.key_vault_uri
  common_tags = {
    workload            = "yahoo-mail-mcp"
    environment         = var.environment
    region              = var.location
    managed-by          = "terraform"
    repository          = var.repository
    owner               = var.owner
    data-classification = "confidential"
  }
}

check "custom_domain_is_subdomain" {
  assert {
    condition = (
      var.mcp_fqdn != trimsuffix(var.route53_zone_name, ".") &&
      endswith(var.mcp_fqdn, ".${trimsuffix(var.route53_zone_name, ".")}")
    )
    error_message = "mcp_fqdn must be a non-apex name beneath route53_zone_name; an apex CNAME is not supported."
  }
}

check "cloudfront_cutover_dependencies" {
  assert {
    condition     = !var.cloudfront_route53_cutover || (var.cloudfront_enabled && var.origin_protection_enabled)
    error_message = "cloudfront_route53_cutover requires CloudFront and origin protection to be enabled."
  }
}

check "origin_protection_dependencies" {
  assert {
    condition     = !var.origin_protection_enabled || var.cloudfront_enabled
    error_message = "origin_protection_enabled requires cloudfront_enabled so valid public traffic receives the verification header."
  }
}

check "caf_container_app_name" {
  assert {
    condition     = local.container_name == "ca-${var.workload}-${var.environment}-${var.region_code}-${var.instance}"
    error_message = "Container App name does not match the CAF naming matrix."
  }
}
