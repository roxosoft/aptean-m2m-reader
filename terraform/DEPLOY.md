# Deploy ApteanM2MReader infrastructure

This stack is applied by **Terraform Cloud** (VCS-driven) against a dedicated
Azure subscription. GitHub Actions only builds and pushes the container image.

| | |
| --- | --- |
| Azure subscription | `074d3945-8b96-48a9-8c63-d856b0a66686` |
| Entra tenant | `14dbca6c-14e9-41a7-9cb2-9b72bf45b508` |
| Region | `eastus` |
| GitHub repo | `roxosoft/aptean-m2m-reader` |
| Terraform Cloud org | `JETechnologySolutions` |
| TFC project | `Aptean M2M Sync` |
| Workspace | `aptean-m2m-reader-prod` |
| Working directory | `terraform` |

Terraform Cloud cannot create its own first Azure identity. Complete steps 1–5
once, then every later change to `terraform/` is a VCS plan/apply.

## 1. Terraform Cloud workspace

1. Sign in to [app.terraform.io](https://app.terraform.io) and create
   organization **JETechnologySolutions** if it does not exist.
2. Create project **Aptean M2M Sync** if it does not exist.
3. Create workspace **aptean-m2m-reader-prod** in that project.
4. Connect it to GitHub repo **roxosoft/aptean-m2m-reader** (VCS-driven).
5. Set **Terraform Working Directory** to `terraform`.

The Entra federated-credential `subject` must use the project name
**exactly** (`Aptean M2M Sync`, including spaces).

Recommended: enable speculative plans on pull requests. Apply on merge to
`main` (auto-apply is optional).

## 2. Entra app for Terraform Cloud (dynamic credentials)

In tenant `14dbca6c-14e9-41a7-9cb2-9b72bf45b508`, create an app registration
that Terraform Cloud will impersonate via OIDC. No client secret.

```bash
SUBSCRIPTION_ID="074d3945-8b96-48a9-8c63-d856b0a66686"
TENANT_ID="14dbca6c-14e9-41a7-9cb2-9b72bf45b508"
TFC_ORG="JETechnologySolutions"
TFC_PROJECT="Aptean M2M Sync"
TFC_WORKSPACE="aptean-m2m-reader-prod"

az login --tenant "$TENANT_ID"
az account set --subscription "$SUBSCRIPTION_ID"

APP_ID=$(az ad app create --display-name "tfc-aptean-m2m-reader-prod" --query appId -o tsv)
az ad sp create --id "$APP_ID"

for PHASE in plan apply; do
  az ad app federated-credential create --id "$APP_ID" --parameters "{
    \"name\": \"tfc-${PHASE}\",
    \"issuer\": \"https://app.terraform.io\",
    \"subject\": \"organization:${TFC_ORG}:project:${TFC_PROJECT}:workspace:${TFC_WORKSPACE}:run_phase:${PHASE}\",
    \"audiences\": [\"api://AzureADTokenExchange\"]
  }"
done

az role assignment create --assignee "$APP_ID" --role "Contributor" --scope "/subscriptions/${SUBSCRIPTION_ID}"
az role assignment create --assignee "$APP_ID" --role "User Access Administrator" --scope "/subscriptions/${SUBSCRIPTION_ID}"

echo "TFC_AZURE_RUN_CLIENT_ID=${APP_ID}"
```

`User Access Administrator` is required so Terraform can create role
assignments (AcrPull, Key Vault Secrets User, GitHub Actions AcrPush, …).

## 3. Workspace environment variables

In the TFC workspace, **Environment Variables**:

| Key | Value | Sensitive |
| --- | --- | --- |
| `TFC_AZURE_PROVIDER_AUTH` | `true` | no |
| `TFC_AZURE_RUN_CLIENT_ID` | app id from step 2 | no |
| `ARM_TENANT_ID` | `14dbca6c-14e9-41a7-9cb2-9b72bf45b508` | no |
| `ARM_SUBSCRIPTION_ID` | `074d3945-8b96-48a9-8c63-d856b0a66686` | no |

Do not set `ARM_CLIENT_SECRET`.

## 4. Workspace Terraform variables

Resource names default to `rg-jet-m2m-prod`, `acrjetm2mprod`,
`kv-jet-m2m-prod`, `stjetm2mprod`, `cae-jet-m2m-prod`,
`ca-aptean-m2m-reader-prod`, `log-jet-m2m-prod`. Override them in the workspace
only if a globally unique name is already taken.

**Required** (mark all four **Sensitive**):

| Variable | Description |
| --- | --- |
| `aptean_company_id` | APICONFIG Company ID |
| `aptean_tenant` | Aptean tenant name |
| `aptean_client_id` | API client id |
| `aptean_client_secret` | API client secret |

Optional: `aptean_base_url`, `aptean_context_path`, `aptean_object_name`,
`job_cron_expression` (default `0 1 * * 1-5`, 01:00 UTC Mon–Fri).

## 5. First apply

Push these Terraform files to `main` (or queue a run in the UI). The first
apply creates:

- Resource group, ACR (admin disabled), Log Analytics, Container Apps env
- Key Vault (RBAC) and Aptean secrets
- Storage account + private `vendors` container
- User-assigned identity for the job (AcrPull, Key Vault Secrets User, Storage Blob Data Contributor)
- Scheduled Container Apps Job (`:latest`)
- User-assigned identity + GitHub federated credential for Actions

If the job resource fails because `:latest` is not in ACR yet, continue with
step 6, run the workflow once, then re-run apply (or start the job manually).

## 6. GitHub Actions variables

Copy Terraform outputs into the GitHub repo **Settings → Secrets and variables
→ Actions → Variables** (not secrets):

| Variable | Terraform output / value |
| --- | --- |
| `AZURE_CLIENT_ID` | `github_actions_client_id` |
| `AZURE_TENANT_ID` | `14dbca6c-14e9-41a7-9cb2-9b72bf45b508` |
| `AZURE_SUBSCRIPTION_ID` | `074d3945-8b96-48a9-8c63-d856b0a66686` |
| `ACR_LOGIN_SERVER` | `acr_login_server` (e.g. `acrjetm2mprod.azurecr.io`) |
| `RESOURCE_GROUP_NAME` | `resource_group_name` |
| `CONTAINER_APP_JOB_NAME` | `container_app_job_name` |

No long-lived Azure credentials are stored in GitHub.

The GitHub OIDC federated credential subject must match the token **exactly**.
This repository uses GitHub’s immutable subject format:

`repo:roxosoft@4310047/aptean-m2m-reader@1363178896:ref:refs/heads/main`

A name-only subject (`repo:roxosoft/aptean-m2m-reader:ref:refs/heads/main`)
fails with `AADSTS700213`. The owner/repo IDs come from the workflow log
under **Federated token details**.

## 7. First image, then the job is live

1. Run **Build and push** (workflow_dispatch) on `main`, or push a change under
   `src/ApteanM2MReader` / `Dockerfile`.
2. The workflow tags `aptean-m2m-reader:latest` and `:1.0.<run_number>`, then
   runs `az containerapp job update --image …:latest` so the next execution
   pulls the new digest.
3. Optional smoke test:

   ```bash
   az containerapp job start \
     --name ca-aptean-m2m-reader-prod \
     --resource-group rg-jet-m2m-prod
   ```

The schedule is **01:00 UTC, Monday–Friday**. Each successful run writes
`vendors-yyyyMMdd-HHmmss.xlsx` to the `vendors` blob container.

## Day-two operations

- **App / image:** merge to `main`; Actions builds, pushes, and refreshes the job.
- **Infra:** change files under `terraform/`; Terraform Cloud plans (and
  applies, if enabled) from VCS.
- **Secrets:** update the sensitive TFC variables and apply so Key Vault stays
  in sync. Do not put Aptean secrets in GitHub or in the image.
- **Manual run:** `az containerapp job start` as above.
- **Logs:** Container Apps Job execution logs in the Azure portal, or
  Log Analytics workspace `log-jet-m2m-prod`.
