variable "workload" {
  type    = string
  default = "ymcp"

  validation {
    condition     = can(regex("^[a-z][a-z0-9]{2,7}$", var.workload))
    error_message = "Workload must be 3-8 lowercase alphanumeric characters."
  }
}

variable "environment" {
  type    = string
  default = "prod"

  validation {
    condition     = contains(["dev", "test", "stage", "prod"], var.environment)
    error_message = "Environment must be dev, test, stage, or prod."
  }
}

variable "location" {
  type    = string
  default = "australiaeast"
}

variable "region_code" {
  type    = string
  default = "aue"

  validation {
    condition     = can(regex("^[a-z]{3,4}$", var.region_code))
    error_message = "Region code must contain 3-4 lowercase letters."
  }
}

variable "instance" {
  type    = string
  default = "001"

  validation {
    condition     = can(regex("^[0-9]{3}$", var.instance))
    error_message = "Instance must contain exactly three digits."
  }
}

variable "repository" {
  type    = string
  default = "YahooMailMcp"
}

variable "owner" {
  type    = string
  default = "platform"

  validation {
    condition     = !strcontains(var.owner, "@")
    error_message = "Owner must be a team or role, not an email address."
  }
}

variable "image_repository" {
  type    = string
  default = "yahoo-mail-mcp"

  validation {
    condition     = can(regex("^[a-z0-9]+(?:[._/-][a-z0-9]+)*$", var.image_repository))
    error_message = "Image repository must be a lowercase OCI repository name."
  }
}

variable "image_digest" {
  description = "Immutable sha256 image digest, including the sha256: prefix."
  type        = string

  validation {
    condition     = can(regex("^sha256:[0-9a-f]{64}$", var.image_digest))
    error_message = "image_digest must be an immutable sha256 digest."
  }
}

variable "mcp_fqdn" {
  description = "Public MCP hostname managed in Route 53 and bound to the Container App."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+$", var.mcp_fqdn))
    error_message = "mcp_fqdn must be a valid lowercase FQDN without a trailing dot."
  }
}

variable "mcp_oauth_enabled" {
  description = "Enable Entra OAuth alongside the static MCP API token."
  type        = bool
  default     = false
}

variable "mcp_oauth_tenant_id" {
  description = "Microsoft Entra tenant that issues MCP access tokens."
  type        = string
  default     = null
  nullable    = true

  validation {
    condition     = !var.mcp_oauth_enabled || can(regex("^[0-9a-fA-F-]{36}$", coalesce(var.mcp_oauth_tenant_id, "")))
    error_message = "mcp_oauth_tenant_id must be a GUID when OAuth is enabled."
  }
}

variable "mcp_oauth_audience" {
  description = "Application/client GUID expected in the Entra access-token aud claim."
  type        = string
  default     = null
  nullable    = true

  validation {
    condition     = !var.mcp_oauth_enabled || can(regex("^[0-9a-fA-F-]{36}$", coalesce(var.mcp_oauth_audience, "")))
    error_message = "mcp_oauth_audience must be an application GUID when OAuth is enabled."
  }
}

variable "mcp_oauth_required_scope" {
  type    = string
  default = "access_as_user"

  validation {
    condition     = can(regex("^[A-Za-z0-9._-]+$", var.mcp_oauth_required_scope))
    error_message = "mcp_oauth_required_scope must be one scope name."
  }
}

variable "route53_zone_name" {
  description = "Existing public Route 53 hosted zone name."
  type        = string
}

variable "route53_ttl" {
  type    = number
  default = 60

  validation {
    condition     = var.route53_ttl >= 30 && var.route53_ttl <= 86400
    error_message = "Route 53 TTL must be between 30 and 86400 seconds."
  }
}

variable "aws_region" {
  type    = string
  default = "ap-southeast-2"
}

variable "cloudfront_enabled" {
  description = "Provision CloudFront, ACM, and AWS WAF in front of the Azure origin. Enable before the Route 53 cutover."
  type        = bool
  default     = false
}

variable "cloudfront_route53_cutover" {
  description = "Point the public Route 53 record at CloudFront. Keep false while validating the generated CloudFront domain."
  type        = bool
  default     = false
}

variable "origin_protection_enabled" {
  description = "Require the CloudFront origin-verification header at the application, excluding Azure health probes."
  type        = bool
  default     = false
}

variable "origin_verification_header_name" {
  description = "Private custom header that CloudFront adds before forwarding to Azure."
  type        = string
  default     = "X-Origin-Verify"

  validation {
    condition     = can(regex("^[!#$%&'*+.^_`|~0-9A-Za-z-]+$", var.origin_verification_header_name))
    error_message = "origin_verification_header_name must be a valid HTTP header token."
  }
}

variable "origin_verification_header_value" {
  description = "Secret custom-header value. Supply through TF_VAR_origin_verification_header_value; never commit it. The CloudFront API requires Terraform to retain it in encrypted state."
  type        = string
  default     = null
  nullable    = true
  sensitive   = true

  validation {
    condition = (
      !var.cloudfront_enabled ||
      (var.origin_verification_header_value != null && length(var.origin_verification_header_value) >= 32)
    )
    error_message = "origin_verification_header_value must contain at least 32 characters when CloudFront is enabled."
  }
}

variable "waf_rate_limit" {
  description = "Maximum POST /mcp requests per source IP during a five-minute WAF evaluation window."
  type        = number
  default     = 100

  validation {
    condition     = var.waf_rate_limit >= 10 && var.waf_rate_limit <= 2000000000
    error_message = "waf_rate_limit must be between 10 and 2000000000."
  }
}

variable "enable_waf_managed_rules" {
  description = "Enable the selected AWS managed rule groups."
  type        = bool
  default     = true
}

variable "waf_managed_rules_count_mode" {
  description = "Override managed rule groups to Count for short-lived false-positive diagnosis. Keep false normally."
  type        = bool
  default     = false
}

variable "cloudfront_price_class" {
  description = "CloudFront edge-location price class for pay-as-you-go operation."
  type        = string
  default     = "PriceClass_100"

  validation {
    condition     = contains(["PriceClass_100", "PriceClass_200", "PriceClass_All"], var.cloudfront_price_class)
    error_message = "cloudfront_price_class must be PriceClass_100, PriceClass_200, or PriceClass_All."
  }
}

variable "tf_state_bucket" {
  description = "Existing encrypted S3 bucket that stores both Terraform states."
  type        = string
}

variable "foundation_state_key" {
  type    = string
  default = "yahoo-mail-mcp/prod/foundation.tfstate"
}

variable "use_oidc" {
  type    = bool
  default = false
}
