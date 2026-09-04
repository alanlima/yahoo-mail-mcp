resource "aws_acm_certificate" "cloudfront" {
  count    = var.cloudfront_enabled ? 1 : 0
  provider = aws.us_east_1

  domain_name       = var.mcp_fqdn
  validation_method = "DNS"

  tags = local.common_tags

  lifecycle {
    create_before_destroy = true
  }
}

resource "aws_route53_record" "cloudfront_certificate_validation" {
  for_each = var.cloudfront_enabled ? {
    for option in aws_acm_certificate.cloudfront[0].domain_validation_options : option.domain_name => {
      name   = option.resource_record_name
      record = option.resource_record_value
      type   = option.resource_record_type
    }
  } : {}

  zone_id         = data.aws_route53_zone.main.zone_id
  name            = each.value.name
  type            = each.value.type
  ttl             = var.route53_ttl
  records         = [each.value.record]
  allow_overwrite = true
}

resource "aws_acm_certificate_validation" "cloudfront" {
  count    = var.cloudfront_enabled ? 1 : 0
  provider = aws.us_east_1

  certificate_arn         = aws_acm_certificate.cloudfront[0].arn
  validation_record_fqdns = [for record in aws_route53_record.cloudfront_certificate_validation : record.fqdn]
}
