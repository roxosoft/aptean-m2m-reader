variable "aptean_base_url" {
  description = "Aptean M2M Web API base URL"
  type        = string
  default     = "https://apps.m2m.apteangovcloud.com"
}

variable "aptean_context_path" {
  description = "Aptean API context path"
  type        = string
  default     = "webapi"
}

variable "aptean_object_name" {
  description = "Aptean object name (e.g. Vendor)"
  type        = string
  default     = "Vendor"
}

variable "aptean_company_id" {
  description = "Aptean Company ID (APICLIENT / APICONFIG)"
  type        = string
  sensitive   = true
}

variable "aptean_tenant" {
  description = "Aptean tenant name"
  type        = string
  sensitive   = true
}

variable "aptean_client_id" {
  description = "Aptean API client id"
  type        = string
  sensitive   = true
}

variable "aptean_client_secret" {
  description = "Aptean API client secret"
  type        = string
  sensitive   = true
}

variable "billcom_base_url" {
  description = "BillCom Web API base URL"
  type        = string
  default     = "https://gateway.prod.bill.com/connect/v3/"
}

variable "billcom_devkey" {
  description = "BillCom Dev Key"
  type        = string
  sensitive   = true
}

variable "billcom_organization_id" {
  description = "BillCom Organization ID"
  type        = string
  sensitive   = true
}

variable "billcom_password" {
  description = "BillCom API Password"
  type        = string
  sensitive   = true
}

variable "billcom_username" {
  description = "BillCom API Username"
  type        = string
  sensitive   = true
}
