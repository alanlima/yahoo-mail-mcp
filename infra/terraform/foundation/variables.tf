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

  validation {
    condition     = contains(keys(local.allowed_region_codes), var.location)
    error_message = "Location must have an explicitly approved region code."
  }
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

variable "global_suffix" {
  description = "Stable four-character non-secret suffix assigned to this deployment."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9]{4}$", var.global_suffix))
    error_message = "Global suffix must contain four lowercase alphanumeric characters."
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

variable "log_retention_days" {
  type    = number
  default = 30

  validation {
    condition     = var.log_retention_days >= 30 && var.log_retention_days <= 730
    error_message = "Log retention must be between 30 and 730 days."
  }
}

variable "alert_email" {
  description = "Optional non-secret operations receiver."
  type        = string
  default     = null
  nullable    = true
}

variable "alerts_enabled" {
  type    = bool
  default = true
}

variable "key_vault_public_network_access_enabled" {
  type    = bool
  default = true
}

variable "use_oidc" {
  type    = bool
  default = false
}
