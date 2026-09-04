terraform {
  required_version = ">= 1.14.0, < 2.0.0"

  backend "s3" {
    region = "ap-southeast-2"
    bucket = "lima-terraform-states"
    key    = "yahoo-mail-mcp/prod/foundation.tfstate"
  }

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.3"
    }
    azapi = {
      source  = "azure/azapi"
      version = "~> 2.12"
    }
  }
}
