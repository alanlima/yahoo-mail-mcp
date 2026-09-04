output "container_app_name" {
  value = azurerm_container_app.main.name
}

output "container_app_default_fqdn" {
  value = azurerm_container_app.main.ingress[0].fqdn
}

output "mcp_url" {
  value = "https://${var.mcp_fqdn}/mcp"
}

output "health_url" {
  description = "Direct Azure probe URL. Health routes are intentionally blocked by the CloudFront WAF allowlist."
  value       = "https://${azurerm_container_app.main.ingress[0].fqdn}/health/ready"
}

output "deployed_image" {
  value = local.image
}

output "cloudfront_distribution_id" {
  value = var.cloudfront_enabled ? aws_cloudfront_distribution.mcp[0].id : null
}

output "cloudfront_distribution_domain_name" {
  value = var.cloudfront_enabled ? aws_cloudfront_distribution.mcp[0].domain_name : null
}

output "waf_web_acl_arn" {
  value = var.cloudfront_enabled ? aws_wafv2_web_acl.mcp[0].arn : null
}

output "mcp_public_url" {
  value = "https://${var.mcp_fqdn}/mcp"
}
