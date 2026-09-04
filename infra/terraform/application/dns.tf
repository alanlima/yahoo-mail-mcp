data "aws_route53_zone" "main" {
  name         = trimsuffix(var.route53_zone_name, ".")
  private_zone = false
}

locals {
  zone_name        = trimsuffix(data.aws_route53_zone.main.name, ".")
  relative_name    = trimsuffix(var.mcp_fqdn, ".${local.zone_name}")
  asuid_record     = "asuid.${local.relative_name}"
  certificate_name = "mc-${var.workload}-${var.environment}-${var.region_code}-${var.instance}"
}

resource "aws_route53_record" "mcp" {
  zone_id = data.aws_route53_zone.main.zone_id
  name    = var.mcp_fqdn
  type    = var.cloudfront_route53_cutover ? "A" : "CNAME"
  ttl     = var.cloudfront_route53_cutover ? null : var.route53_ttl
  records = var.cloudfront_route53_cutover ? null : [azurerm_container_app.main.ingress[0].fqdn]

  dynamic "alias" {
    for_each = var.cloudfront_route53_cutover ? [1] : []
    content {
      name                   = aws_cloudfront_distribution.mcp[0].domain_name
      zone_id                = aws_cloudfront_distribution.mcp[0].hosted_zone_id
      evaluate_target_health = false
    }
  }
}

resource "aws_route53_record" "mcp_ipv6" {
  count = var.cloudfront_route53_cutover ? 1 : 0

  zone_id = data.aws_route53_zone.main.zone_id
  name    = var.mcp_fqdn
  type    = "AAAA"

  alias {
    name                   = aws_cloudfront_distribution.mcp[0].domain_name
    zone_id                = aws_cloudfront_distribution.mcp[0].hosted_zone_id
    evaluate_target_health = false
  }

  # Route 53 forbids an AAAA record while the legacy same-name CNAME exists.
  # Wait until Terraform has replaced that CNAME with the CloudFront A alias.
  depends_on = [aws_route53_record.mcp]
}

resource "aws_route53_record" "verification" {
  zone_id = data.aws_route53_zone.main.zone_id
  name    = local.asuid_record
  type    = "TXT"
  ttl     = var.route53_ttl
  records = [azurerm_container_app.main.custom_domain_verification_id]
}

resource "azurerm_container_app_custom_domain" "main" {
  name                     = var.mcp_fqdn
  container_app_id         = azurerm_container_app.main.id
  certificate_binding_type = "Disabled"

  lifecycle {
    ignore_changes = [certificate_binding_type, container_app_environment_certificate_id]
  }

  depends_on = [aws_route53_record.mcp, aws_route53_record.verification]
}

resource "azurerm_container_app_environment_managed_certificate" "main" {
  name                         = local.certificate_name
  container_app_environment_id = data.terraform_remote_state.foundation.outputs.container_app_environment_id
  subject_name                 = var.mcp_fqdn
  domain_control_validation    = "CNAME"
  tags                         = local.common_tags

  depends_on = [azurerm_container_app_custom_domain.main]
}

# Azure requires an unbound hostname before managed-certificate issuance. The AzureRM
# custom-domain resource cannot express add -> issue -> bind without a dependency cycle,
# so AzAPI performs only the final binding patch after both resources exist.
resource "azapi_update_resource" "custom_domain_certificate_binding" {
  type        = "Microsoft.App/containerApps@2025-07-01"
  resource_id = azurerm_container_app.main.id

  body = {
    properties = {
      configuration = {
        ingress = {
          customDomains = [
            {
              name          = var.mcp_fqdn
              bindingType   = "SniEnabled"
              certificateId = azurerm_container_app_environment_managed_certificate.main.id
            }
          ]
        }
      }
    }
  }

  depends_on = [azurerm_container_app_environment_managed_certificate.main]
}
