provider "azurerm" {
  features {}
  use_oidc = var.use_oidc
}

provider "azapi" {

}

data "azurerm_client_config" "current" {}
