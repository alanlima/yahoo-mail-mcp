locals {
  allowed_region_codes = {
    australiaeast      = "aue"
    australiasoutheast = "ause"
  }
  base         = "${var.workload}-${var.environment}-${var.region_code}-${var.instance}"
  compact_base = "${var.workload}${var.environment}${var.region_code}${var.instance}"
  names = {
    resource_group = "rg-${local.base}"
    # container_registry = "cr${local.compact_base}${var.global_suffix}"
    container_registry = "cr${local.compact_base}"
    container_env      = "cae-${local.base}"
    # key_vault          = "kv-${var.workload}-${var.environment}-${var.region_code}-01${var.global_suffix}"
    key_vault     = "kv-${var.workload}-${var.environment}-${var.region_code}-01"
    identity      = "id-${local.base}"
    log_analytics = "log-${local.base}"
    app_insights  = "appi-${local.base}"
    action_group  = "ag-${local.base}"
  }
  common_tags = {
    workload            = "yahoo-mail-mcp"
    environment         = var.environment
    region              = var.location
    managed-by          = "terraform"
    repository          = var.repository
    owner               = var.owner
    data-classification = "confidential"
  }
}

check "region_code_matches_location" {
  assert {
    condition     = var.region_code == local.allowed_region_codes[var.location]
    error_message = "region_code must match the centralized location map."
  }
}

check "caf_name_matrix" {
  assert {
    condition = (
      local.names.resource_group == "rg-${var.workload}-${var.environment}-${var.region_code}-${var.instance}" &&
      startswith(local.names.container_env, "cae-") &&
      startswith(local.names.identity, "id-") &&
      startswith(local.names.log_analytics, "log-") &&
      startswith(local.names.app_insights, "appi-") &&
      startswith(local.names.action_group, "ag-")
    )
    error_message = "Generated names do not match the CAF naming matrix."
  }
}

check "global_name_constraints" {
  assert {
    condition = (
      length(local.names.container_registry) >= 5 &&
      length(local.names.container_registry) <= 50 &&
      can(regex("^[a-z0-9]+$", local.names.container_registry)) &&
      length(local.names.key_vault) >= 3 &&
      length(local.names.key_vault) <= 24 &&
      can(regex("^[a-z0-9-]+$", local.names.key_vault))
    )
    error_message = "Generated ACR or Key Vault name violates Azure constraints."
  }
}

resource "azurerm_resource_group" "main" {
  name     = local.names.resource_group
  location = var.location
  tags     = local.common_tags
}

resource "azurerm_container_registry" "main" {
  name                          = local.names.container_registry
  resource_group_name           = azurerm_resource_group.main.name
  location                      = azurerm_resource_group.main.location
  sku                           = "Basic"
  admin_enabled                 = false
  public_network_access_enabled = true
  tags                          = local.common_tags

  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_log_analytics_workspace" "main" {
  name                = local.names.log_analytics
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = "PerGB2018"
  retention_in_days   = var.log_retention_days
  tags                = local.common_tags

  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_application_insights" "main" {
  name                = local.names.app_insights
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  workspace_id        = azurerm_log_analytics_workspace.main.id
  application_type    = "web"
  retention_in_days   = var.log_retention_days
  tags                = local.common_tags
}

resource "azurerm_container_app_environment" "main" {
  name                       = local.names.container_env
  resource_group_name        = azurerm_resource_group.main.name
  location                   = azurerm_resource_group.main.location
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id
  logs_destination           = "log-analytics"

  workload_profile {
    maximum_count         = 5
    minimum_count         = 0
    name                  = "Consumption"
    workload_profile_type = "Consumption"
  }
  tags = local.common_tags
}

resource "azapi_resource" "aspire_dashboard" {
  type      = "Microsoft.App/managedEnvironments/dotNetComponents@2025-02-02-preview"
  name      = "aspire-dashboard"
  parent_id = azurerm_container_app_environment.main.id
  body = {
    properties = {
      componentType = "AspireDashboard"
    }
  }
  depends_on = [azurerm_container_app_environment.main]
}

resource "azurerm_user_assigned_identity" "app" {
  name                = local.names.identity
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  tags                = local.common_tags
}

resource "azurerm_key_vault" "main" {
  name                            = local.names.key_vault
  resource_group_name             = azurerm_resource_group.main.name
  location                        = azurerm_resource_group.main.location
  tenant_id                       = data.azurerm_client_config.current.tenant_id
  sku_name                        = "standard"
  rbac_authorization_enabled      = true
  enabled_for_disk_encryption     = false
  enabled_for_deployment          = false
  enabled_for_template_deployment = false
  purge_protection_enabled        = true
  soft_delete_retention_days      = 90
  public_network_access_enabled   = var.key_vault_public_network_access_enabled
  tags                            = local.common_tags

  lifecycle {
    prevent_destroy = true
  }
}

data "azurerm_subscription" "current" {}

data "azurerm_role_definition" "acr_pull" {
  name  = "AcrPull"
  scope = data.azurerm_subscription.current.id

}

data "azurerm_role_definition" "key_vault_secrets_user" {
  name  = "Key Vault Secrets User"
  scope = data.azurerm_subscription.current.id
}

resource "azurerm_role_assignment" "acr_pull" {
  scope              = azurerm_container_registry.main.id
  role_definition_id = data.azurerm_role_definition.acr_pull.id
  principal_id       = azurerm_user_assigned_identity.app.principal_id
  principal_type     = "ServicePrincipal"
}

resource "azurerm_role_assignment" "key_vault_secrets_user" {
  scope              = azurerm_key_vault.main.id
  role_definition_id = data.azurerm_role_definition.key_vault_secrets_user.id
  principal_id       = azurerm_user_assigned_identity.app.principal_id
  principal_type     = "ServicePrincipal"
}
