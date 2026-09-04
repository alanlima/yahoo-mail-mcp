terraform {
  required_version = ">= 1.14.0, < 2.0.0"

  backend "s3" {}

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
