resource "aws_wafv2_web_acl" "mcp" {
  count    = var.cloudfront_enabled ? 1 : 0
  provider = aws.us_east_1

  name        = "waf-${local.base}"
  description = "Default-deny edge protection for the YahooMailMcp public API"
  scope       = "CLOUDFRONT"

  default_action {
    block {}
  }

  dynamic "rule" {
    for_each = var.enable_waf_managed_rules ? [
      { name = "AmazonIpReputation", priority = 20, group = "AWSManagedRulesAmazonIpReputationList", metric = "amazon-ip-reputation" },
      { name = "KnownBadInputs", priority = 30, group = "AWSManagedRulesKnownBadInputsRuleSet", metric = "known-bad-inputs" },
      { name = "CommonProtections", priority = 40, group = "AWSManagedRulesCommonRuleSet", metric = "common-protections" }
    ] : []

    content {
      name     = rule.value.name
      priority = rule.value.priority

      override_action {
        dynamic "count" {
          for_each = var.waf_managed_rules_count_mode ? [1] : []
          content {}
        }
        dynamic "none" {
          for_each = var.waf_managed_rules_count_mode ? [] : [1]
          content {}
        }
      }

      statement {
        managed_rule_group_statement {
          name        = rule.value.group
          vendor_name = "AWS"

          dynamic "rule_action_override" {
            for_each = rule.value.group == "AWSManagedRulesCommonRuleSet" ? [1] : []
            content {
              name = "SizeRestrictions_BODY"
              action_to_use {
                count {}
              }
            }
          }
        }
      }

      visibility_config {
        cloudwatch_metrics_enabled = true
        metric_name                = rule.value.metric
        sampled_requests_enabled   = false
      }
    }
  }

  rule {
    name     = "McpRateLimit"
    priority = 50

    action {
      block {}
    }

    statement {
      rate_based_statement {
        aggregate_key_type    = "IP"
        evaluation_window_sec = 300
        limit                 = var.waf_rate_limit

        scope_down_statement {
          and_statement {
            statement {
              byte_match_statement {
                positional_constraint = "EXACTLY"
                search_string         = "/mcp"
                field_to_match {
                  uri_path {}
                }
                text_transformation {
                  priority = 0
                  type     = "NONE"
                }
              }
            }
            statement {
              byte_match_statement {
                positional_constraint = "EXACTLY"
                search_string         = "POST"
                field_to_match {
                  method {}
                }
                text_transformation {
                  priority = 0
                  type     = "NONE"
                }
              }
            }
          }
        }
      }
    }

    visibility_config {
      cloudwatch_metrics_enabled = true
      metric_name                = "mcp-rate-limit"
      sampled_requests_enabled   = false
    }
  }

  rule {
    name     = "AllowPublicSurface"
    priority = 90

    action {
      allow {}
    }

    statement {
      or_statement {
        statement {
          and_statement {
            statement {
              byte_match_statement {
                positional_constraint = "EXACTLY"
                search_string         = "/mcp"
                field_to_match {
                  uri_path {}
                }
                text_transformation {
                  priority = 0
                  type     = "NONE"
                }
              }
            }
            statement {
              byte_match_statement {
                positional_constraint = "EXACTLY"
                search_string         = "POST"
                field_to_match {
                  method {}
                }
                text_transformation {
                  priority = 0
                  type     = "NONE"
                }
              }
            }
          }
        }
        statement {
          and_statement {
            statement {
              byte_match_statement {
                positional_constraint = "EXACTLY"
                search_string         = "GET"
                field_to_match {
                  method {}
                }
                text_transformation {
                  priority = 0
                  type     = "NONE"
                }
              }
            }
            statement {
              or_statement {
                statement {
                  byte_match_statement {
                    positional_constraint = "EXACTLY"
                    search_string         = "/"
                    field_to_match {
                      uri_path {}
                    }
                    text_transformation {
                      priority = 0
                      type     = "NONE"
                    }
                  }
                }
                statement {
                  byte_match_statement {
                    positional_constraint = "EXACTLY"
                    search_string         = "/.well-known/oauth-protected-resource"
                    field_to_match {
                      uri_path {}
                    }
                    text_transformation {
                      priority = 0
                      type     = "NONE"
                    }
                  }
                }
              }
            }
          }
        }
      }
    }

    visibility_config {
      cloudwatch_metrics_enabled = true
      metric_name                = "allow-public-surface"
      sampled_requests_enabled   = false
    }
  }

  visibility_config {
    cloudwatch_metrics_enabled = true
    metric_name                = "yahoo-mail-mcp"
    sampled_requests_enabled   = false
  }

  tags = local.common_tags
}
