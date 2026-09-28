resource "azurerm_key_vault_secret" "aptean_base_url" {
  name         = "Aptean--BaseUrl"
  value        = var.aptean_base_url
  key_vault_id = azurerm_key_vault.kv.id
  depends_on   = [time_sleep.kv_rbac]
}

resource "azurerm_key_vault_secret" "aptean_context_path" {
  name         = "Aptean--ContextPath"
  value        = var.aptean_context_path
  key_vault_id = azurerm_key_vault.kv.id
  depends_on   = [time_sleep.kv_rbac]
}

resource "azurerm_key_vault_secret" "aptean_object_name" {
  name         = "Aptean--ObjectName"
  value        = var.aptean_object_name
  key_vault_id = azurerm_key_vault.kv.id
  depends_on   = [time_sleep.kv_rbac]
}

resource "azurerm_key_vault_secret" "aptean_company_id" {
  name         = "Aptean--CompanyId"
  value        = var.aptean_company_id
  key_vault_id = azurerm_key_vault.kv.id
  content_type = "text/plain"
  depends_on   = [time_sleep.kv_rbac]
}

resource "azurerm_key_vault_secret" "aptean_tenant" {
  name         = "Aptean--Tenant"
  value        = var.aptean_tenant
  key_vault_id = azurerm_key_vault.kv.id
  content_type = "text/plain"
  depends_on   = [time_sleep.kv_rbac]
}

resource "azurerm_key_vault_secret" "aptean_client_id" {
  name         = "Aptean--ClientId"
  value        = var.aptean_client_id
  key_vault_id = azurerm_key_vault.kv.id
  content_type = "text/plain"
  depends_on   = [time_sleep.kv_rbac]
}

resource "azurerm_key_vault_secret" "aptean_client_secret" {
  name         = "Aptean--ClientSecret"
  value        = var.aptean_client_secret
  key_vault_id = azurerm_key_vault.kv.id
  content_type = "text/plain"
  depends_on   = [time_sleep.kv_rbac]
}

resource "azurerm_key_vault_secret" "billcom_base_url" {
  # Appsettings section is BillDotCom (Key Vault -- maps to :).
  name         = "BillDotCom--BaseUrl"
  value        = var.billcom_base_url
  key_vault_id = azurerm_key_vault.kv.id
  depends_on   = [time_sleep.kv_rbac]
}

resource "azurerm_key_vault_secret" "billcom_devkey" {
  name         = "BillDotCom--DevKey"
  value        = var.billcom_devkey
  key_vault_id = azurerm_key_vault.kv.id
  content_type = "text/plain"
  depends_on   = [time_sleep.kv_rbac]
}

resource "azurerm_key_vault_secret" "billcom_organization_id" {
  name         = "BillDotCom--OrganizationId"
  value        = var.billcom_organization_id
  key_vault_id = azurerm_key_vault.kv.id
  content_type = "text/plain"
  depends_on   = [time_sleep.kv_rbac]
}

resource "azurerm_key_vault_secret" "billcom_username" {
  name         = "BillDotCom--Username"
  value        = var.billcom_username
  key_vault_id = azurerm_key_vault.kv.id
  content_type = "text/plain"
  depends_on   = [time_sleep.kv_rbac]
}

resource "azurerm_key_vault_secret" "billcom_password" {
  name         = "BillDotCom--Password"
  value        = var.billcom_password
  key_vault_id = azurerm_key_vault.kv.id
  content_type = "text/plain"
  depends_on   = [time_sleep.kv_rbac]
}
