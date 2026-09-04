provider "azurerm" {
  features {}
  use_oidc = var.use_oidc
}

provider "azapi" {
  use_oidc = var.use_oidc
}

provider "aws" {
  region = var.aws_region
}

provider "aws" {
  alias  = "us_east_1"
  region = "us-east-1"
}

data "terraform_remote_state" "foundation" {
  backend = "s3"
  config = {
    bucket       = var.tf_state_bucket
    key          = var.foundation_state_key
    region       = var.aws_region
    encrypt      = true
    use_lockfile = true
  }
}
