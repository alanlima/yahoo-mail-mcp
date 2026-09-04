data "aws_cloudfront_cache_policy" "caching_disabled" {
  count = var.cloudfront_enabled ? 1 : 0
  name  = "Managed-CachingDisabled"
}

data "aws_cloudfront_origin_request_policy" "all_viewer_except_host" {
  count = var.cloudfront_enabled ? 1 : 0
  name  = "Managed-AllViewerExceptHostHeader"
}

resource "aws_cloudfront_distribution" "mcp" {
  count = var.cloudfront_enabled ? 1 : 0

  enabled         = true
  is_ipv6_enabled = true
  aliases         = [var.mcp_fqdn]
  comment         = "YahooMailMcp authenticated API edge"
  http_version    = "http2and3"
  price_class     = var.cloudfront_price_class
  web_acl_id      = aws_wafv2_web_acl.mcp[0].arn

  origin {
    domain_name = azurerm_container_app.main.ingress[0].fqdn
    origin_id   = "azure-container-app"

    custom_header {
      name  = var.origin_verification_header_name
      value = var.origin_verification_header_value
    }

    custom_origin_config {
      http_port              = 80
      https_port             = 443
      origin_protocol_policy = "https-only"
      origin_ssl_protocols   = ["TLSv1.2"]
    }
  }

  default_cache_behavior {
    target_origin_id         = "azure-container-app"
    viewer_protocol_policy   = "redirect-to-https"
    allowed_methods          = ["DELETE", "GET", "HEAD", "OPTIONS", "PATCH", "POST", "PUT"]
    cached_methods           = ["GET", "HEAD"]
    compress                 = true
    cache_policy_id          = data.aws_cloudfront_cache_policy.caching_disabled[0].id
    origin_request_policy_id = data.aws_cloudfront_origin_request_policy.all_viewer_except_host[0].id
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  viewer_certificate {
    acm_certificate_arn      = aws_acm_certificate_validation.cloudfront[0].certificate_arn
    minimum_protocol_version = "TLSv1.2_2021"
    ssl_support_method       = "sni-only"
  }

  tags = local.common_tags
}
