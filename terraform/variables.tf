variable "subscription_id" {
  description = "Azure subscription ID for this stack"
  type        = string
  default     = "074d3945-8b96-48a9-8c63-d856b0a66686"
}

variable "location" {
  description = "Azure region"
  type        = string
  default     = "eastus"
}

variable "resource_group_name" {
  description = "Name of the resource group"
  type        = string
  default     = "rg-jet-m2m-prod"
}

variable "acr_name" {
  description = "Azure Container Registry name (globally unique, alphanumeric)"
  type        = string
  default     = "acrjetm2mprod"
}

variable "key_vault_name" {
  description = "Key Vault name (globally unique)"
  type        = string
  default     = "kv-jet-m2m-prod"
}

variable "storage_account_name" {
  description = "Storage account name (globally unique, lowercase alphanumeric)"
  type        = string
  default     = "stjetm2mprod"
}

variable "blob_container_name" {
  description = "Blob container for vendor Excel exports"
  type        = string
  default     = "vendors"
}

variable "log_analytics_workspace_name" {
  description = "Log Analytics workspace name"
  type        = string
  default     = "log-jet-m2m-prod"
}

variable "container_app_environment_name" {
  description = "Container Apps Environment name"
  type        = string
  default     = "cae-jet-m2m-prod"
}

variable "job_name" {
  description = "Container Apps Job name"
  type        = string
  default     = "ca-aptean-m2m-reader-prod"
}

variable "image_repository" {
  description = "ACR repository name for the reader image"
  type        = string
  default     = "aptean-m2m-reader"
}

variable "job_cron_expression" {
  description = "Cron schedule for the job (UTC)"
  type        = string
  default     = "0 1 * * 1-5"
}

variable "github_repository" {
  description = "GitHub org/repo used for Actions OIDC federation"
  type        = string
  default     = "roxosoft/aptean-m2m-reader"
}

variable "github_owner_id" {
  description = "Immutable GitHub organization ID included in the Actions OIDC subject"
  type        = string
  default     = "4310047"
}

variable "github_repository_id" {
  description = "Immutable GitHub repository ID included in the Actions OIDC subject"
  type        = string
  default     = "1363178896"
}

variable "tags" {
  description = "Tags applied to all resources"
  type        = map(string)
  default = {
    project     = "aptean-m2m-reader"
    environment = "prod"
  }
}
