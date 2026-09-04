terraform {
  required_version = ">= 1.14.0, < 2.0.0"

  backend "s3" {}

  required_providers {
    azapi = {
      source  = "Azure/azapi"
      version = "~> 2.11"
    }
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.3"
    }
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.62"
    }
  }
}
