resource "azurerm_container_app" "main" {
  name                         = local.container_name
  resource_group_name          = data.terraform_remote_state.foundation.outputs.resource_group_name
  container_app_environment_id = data.terraform_remote_state.foundation.outputs.container_app_environment_id
  revision_mode                = "Single"
  workload_profile_name        = "Consumption"
  tags                         = local.common_tags

  identity {
    type         = "UserAssigned"
    identity_ids = [data.terraform_remote_state.foundation.outputs.managed_identity_id]
  }

  registry {
    server   = data.terraform_remote_state.foundation.outputs.container_registry_login_server
    identity = data.terraform_remote_state.foundation.outputs.managed_identity_id
  }

  secret {
    name                = "yahoo-email"
    identity            = data.terraform_remote_state.foundation.outputs.managed_identity_id
    key_vault_secret_id = "${local.vault_uri}secrets/yahoo-email"
  }

  secret {
    name                = "yahoo-app-password"
    identity            = data.terraform_remote_state.foundation.outputs.managed_identity_id
    key_vault_secret_id = "${local.vault_uri}secrets/yahoo-app-password"
  }

  secret {
    name                = "mcp-bearer-token"
    identity            = data.terraform_remote_state.foundation.outputs.managed_identity_id
    key_vault_secret_id = "${local.vault_uri}secrets/mcp-bearer-token"
  }

  secret {
    name                = "cursor-signing-key"
    identity            = data.terraform_remote_state.foundation.outputs.managed_identity_id
    key_vault_secret_id = "${local.vault_uri}secrets/cursor-signing-key"
  }

  dynamic "secret" {
    for_each = var.mcp_oauth_enabled ? [1] : []
    content {
      name                = "mcp-oauth-client-id"
      identity            = data.terraform_remote_state.foundation.outputs.managed_identity_id
      key_vault_secret_id = "${local.vault_uri}secrets/mcp-oauth-client-id"
    }
  }

  dynamic "secret" {
    for_each = var.origin_protection_enabled ? [1] : []
    content {
      name                = "origin-verification-header"
      identity            = data.terraform_remote_state.foundation.outputs.managed_identity_id
      key_vault_secret_id = "${local.vault_uri}secrets/origin-verification-header"
    }
  }

  ingress {
    external_enabled           = true
    allow_insecure_connections = false
    target_port                = 8080
    transport                  = "http"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    min_replicas = 1
    max_replicas = 1

    container {
      name   = "yahoo-mail-mcp"
      image  = local.image
      cpu    = 0.5
      memory = "1Gi"

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }

      env {
        name  = "ASPNETCORE_URLS"
        value = "http://+:8080"
      }

      # env {
      #   name  = "AllowedHosts"
      #   value = "${var.mcp_fqdn};localhost;127.0.0.1"
      # }

      env {
        name  = "AllowedHosts"
        value = "*"
      }

      env {
        name  = "APPLICATIONINSIGHTS_CONNECTION_STRING"
        value = data.terraform_remote_state.foundation.outputs.application_insights_connection_string
      }

      env {
        name        = "Yahoo__Email"
        secret_name = "yahoo-email"
      }

      env {
        name        = "Yahoo__AppPassword"
        secret_name = "yahoo-app-password"
      }

      env {
        name        = "Mcp__BearerToken"
        secret_name = "mcp-bearer-token"
      }

      env {
        name        = "Cursor__SigningKey"
        secret_name = "cursor-signing-key"
      }

      env {
        name  = "Mcp__OAuth__Enabled"
        value = tostring(var.mcp_oauth_enabled)
      }

      dynamic "env" {
        for_each = var.mcp_oauth_enabled ? [1] : []
        content {
          name  = "Mcp__OAuth__TenantId"
          value = var.mcp_oauth_tenant_id
        }
      }

      env {
        name  = "OriginProtection__Enabled"
        value = tostring(var.origin_protection_enabled)
      }

      env {
        name  = "OriginProtection__HeaderName"
        value = var.origin_verification_header_name
      }

      dynamic "env" {
        for_each = var.origin_protection_enabled ? [1] : []
        content {
          name        = "OriginProtection__HeaderValue"
          secret_name = "origin-verification-header"
        }
      }

      dynamic "env" {
        for_each = var.mcp_oauth_enabled ? [1] : []
        content {
          name        = "Mcp__OAuth__ClientId"
          secret_name = "mcp-oauth-client-id"
        }
      }

      dynamic "env" {
        for_each = var.mcp_oauth_enabled ? [1] : []
        content {
          name  = "Mcp__OAuth__Audience"
          value = var.mcp_oauth_audience
        }
      }

      dynamic "env" {
        for_each = var.mcp_oauth_enabled ? [1] : []
        content {
          name  = "Mcp__OAuth__Resource"
          value = "https://${var.mcp_fqdn}/mcp"
        }
      }

      dynamic "env" {
        for_each = var.mcp_oauth_enabled ? [1] : []
        content {
          name  = "Mcp__OAuth__RequiredScope"
          value = var.mcp_oauth_required_scope
        }
      }

      liveness_probe {
        transport               = "HTTP"
        port                    = 8080
        path                    = "/health/live"
        interval_seconds        = 30
        timeout                 = 5
        failure_count_threshold = 3
      }

      readiness_probe {
        transport               = "HTTP"
        port                    = 8080
        path                    = "/health/ready"
        interval_seconds        = 15
        timeout                 = 5
        failure_count_threshold = 3
        success_count_threshold = 1
      }
    }
  }

  depends_on = [
    data.terraform_remote_state.foundation
  ]
}
