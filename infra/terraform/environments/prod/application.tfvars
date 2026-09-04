workload    = "ymcp"
environment = "prod"
location    = "australiaeast"
region_code = "aue"
instance    = "001"
repository  = "YahooMailMcp"
owner       = "platform"

image_repository = "yahoo-mail-mcp"
image_digest     = "sha256:f3f9b43306ba9b8ab06a5a9e45077268422e4f5312788ff29256e3bc7d920717"

mcp_fqdn                 = "mcp.alanlima.cloud"
mcp_oauth_enabled        = true
mcp_oauth_tenant_id      = "b16c65ab-9711-44cd-abf1-24e6180a4609"
mcp_oauth_audience       = "7553b3c9-f602-42a8-b5fe-8a54af0dd3af"
mcp_oauth_required_scope = "access_as_user"
route53_zone_name        = "alanlima.cloud"
route53_ttl              = 60
aws_region               = "ap-southeast-2"

cloudfront_enabled                              = true
cloudfront_route53_cutover                      = true
origin_protection_enabled                       = true
waf_rate_limit                                  = 100
enable_waf_managed_rules                        = true
waf_managed_rules_count_mode                    = false
cloudfront_price_class                          = "PriceClass_100"

# origin_verification_header_value = "X-Origin-Verify"

tf_state_bucket      = "lima-terraform-states"
foundation_state_key = "yahoo-mail-mcp/prod/foundation.tfstate"
use_oidc             = true
