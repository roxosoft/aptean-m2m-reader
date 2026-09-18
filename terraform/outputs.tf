output "resource_group_name" {
  value = azurerm_resource_group.rg.name
}

output "acr_login_server" {
  value = azurerm_container_registry.acr.login_server
}

output "acr_name" {
  value = azurerm_container_registry.acr.name
}

output "container_app_job_name" {
  value = azurerm_container_app_job.reader.name
}

output "key_vault_name" {
  value = azurerm_key_vault.kv.name
}

output "key_vault_uri" {
  value = azurerm_key_vault.kv.vault_uri
}

output "storage_account_name" {
  value = azurerm_storage_account.storage.name
}

output "blob_container_name" {
  value = azurerm_storage_container.vendors.name
}

output "github_actions_client_id" {
  description = "Set this as the GitHub Actions repository variable AZURE_CLIENT_ID"
  value       = azurerm_user_assigned_identity.gha.client_id
}

output "github_actions_identity_name" {
  value = azurerm_user_assigned_identity.gha.name
}
