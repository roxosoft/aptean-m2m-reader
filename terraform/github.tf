resource "azurerm_user_assigned_identity" "gha" {
  name                = "id-gha-aptean-m2m-reader"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  tags                = var.tags
}

resource "azurerm_federated_identity_credential" "gha_main" {
  name                = "github-main"
  resource_group_name = azurerm_resource_group.rg.name
  parent_id           = azurerm_user_assigned_identity.gha.id
  audience            = ["api://AzureADTokenExchange"]
  issuer              = "https://token.actions.githubusercontent.com"
  subject             = "repo:${var.github_repository}:ref:refs/heads/main"
}

resource "azurerm_role_assignment" "gha_acr_push" {
  scope                = azurerm_container_registry.acr.id
  role_definition_name = "AcrPush"
  principal_id         = azurerm_user_assigned_identity.gha.principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "gha_container_apps" {
  scope                = azurerm_container_app_job.reader.id
  role_definition_name = "Contributor"
  principal_id         = azurerm_user_assigned_identity.gha.principal_id
  principal_type       = "ServicePrincipal"
}
