resource "azurerm_monitor_metric_alert" "replicas" {
  name                = "alert-${local.container_name}-replicas"
  resource_group_name = data.terraform_remote_state.foundation.outputs.resource_group_name
  scopes              = [azurerm_container_app.main.id]
  description         = "Container App has no running replicas for five minutes."
  severity            = 1
  frequency           = "PT1M"
  window_size         = "PT5M"
  enabled             = true
  tags                = local.common_tags

  criteria {
    metric_namespace       = "Microsoft.App/containerApps"
    metric_name            = "Replicas"
    aggregation            = "Average"
    operator               = "LessThan"
    threshold              = 1
    skip_metric_validation = true
  }

  action {
    action_group_id = data.terraform_remote_state.foundation.outputs.action_group_id
  }
}

resource "azurerm_monitor_metric_alert" "http_errors" {
  name                = "alert-${local.container_name}-http5xx"
  resource_group_name = data.terraform_remote_state.foundation.outputs.resource_group_name
  scopes              = [azurerm_container_app.main.id]
  description         = "Container App is returning repeated HTTP 5xx responses."
  severity            = 1
  frequency           = "PT1M"
  window_size         = "PT5M"
  enabled             = true
  tags                = local.common_tags

  criteria {
    metric_namespace       = "Microsoft.App/containerApps"
    metric_name            = "Requests"
    aggregation            = "Total"
    operator               = "GreaterThan"
    threshold              = 5
    skip_metric_validation = true

    dimension {
      name     = "StatusCodeCategory"
      operator = "Include"
      values   = ["5xx"]
    }
  }

  action {
    action_group_id = data.terraform_remote_state.foundation.outputs.action_group_id
  }
}
