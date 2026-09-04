resource "azurerm_monitor_action_group" "main" {
  name                = local.names.action_group
  resource_group_name = azurerm_resource_group.main.name
  short_name          = "ymcp-${var.environment}"
  enabled             = var.alerts_enabled
  tags                = local.common_tags

  dynamic "email_receiver" {
    for_each = var.alert_email == null ? [] : [var.alert_email]
    content {
      name                    = "operations"
      email_address           = email_receiver.value
      use_common_alert_schema = true
    }
  }
}
