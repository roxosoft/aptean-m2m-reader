terraform {
  required_version = ">= 1.8.0"

  cloud {
    organization = "JETechnologySolutions"

    workspaces {
      name = "aptean-m2m-reader-prod"
    }
  }

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    time = {
      source  = "hashicorp/time"
      version = "~> 0.12"
    }
  }
}

provider "azurerm" {
  features {}
  use_oidc        = true
  subscription_id = var.subscription_id
}

data "azurerm_client_config" "current" {}
