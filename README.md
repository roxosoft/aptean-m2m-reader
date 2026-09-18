# Aptean M2M Reader

.NET 8 tools for Made2Manage vendor data. The scheduled production job is
**ApteanM2MReader**: it authenticates to the Aptean M2M Web API, fetches vendors,
writes a Bill.com-compatible Excel file, and (in Azure) uploads a timestamped
blob.

## Projects

| Project | Path | Deployed |
| --- | --- | --- |
| [ApteanM2MReader](src/ApteanM2MReader/README.md) | `src/ApteanM2MReader` | Yes — Docker image, Azure Container Apps Job |
| [BillVendorSync](src/BillVendorSync/README.md) | `src/BillVendorSync` | No — interactive laptop tool |

## Local run (reader)

```bash
cd src/ApteanM2MReader
cp appsettings.example.json appsettings.json   # then fill Aptean credentials
dotnet run
dotnet run -- --output ../../vendors.xlsx
```

Configuration is JSON + environment variables (`Aptean__ClientSecret`, …). In
Azure the job loads secrets from Key Vault via managed identity.

## CI/CD

GitHub Actions ([`.github/workflows/build.yml`](.github/workflows/build.yml))
builds and pushes `aptean-m2m-reader:latest` (and `1.0.<run_number>`) to Azure
Container Registry on every push to `main`, then refreshes the Container Apps
Job image.

## Infrastructure

Terraform under [`terraform/`](terraform/) provisions ACR, a Container Apps
environment, the scheduled job, Key Vault, and Blob Storage in a dedicated
resource group. State and apply run in Terraform Cloud.

See [terraform/DEPLOY.md](terraform/DEPLOY.md) for first-time setup
(Terraform Cloud, Azure OIDC, GitHub variables).
