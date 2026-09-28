# Aptean M2M Web API Reader

A .NET 8 console app that authenticates to the Aptean Made2Manage Web API,
fetches vendors, writes a Bill.com-compatible Excel file, optionally uploads a
timestamped blob, and (when Bill.com credentials are configured) matches and
updates Bill.com vendor `companyName` values.

## Scope

1. Obtain an OAuth access token via **Client Credentials**.
2. `GET` all vendors from the M2M Web API (paged list), then enrich each with a detail `GET`.
3. Write `vendors.xlsx` with headers expected by Bill sync (plus State / Phone).
4. Optionally upload that file to Azure Blob Storage.
5. Optionally sync to Bill.com (exact + relaxed updates auto-apply in Production; unmatched M2M vendors are created).

## Credentials

Copy `appsettings.example.json` to `appsettings.json` (gitignored) and fill in
values from Made2Manage **APICLIENT** / **APICONFIG**. `appsettings.json` is
optional: environment variables and Azure Key Vault can supply the same keys.

| Setting | Source |
| --- | --- |
| `BaseUrl` | M2M site host, e.g. `https://apps.m2m.apteangovcloud.com` |
| `ContextPath` | API context path (govcloud default `webapi`) |
| `ObjectName` | APICONFIG object name (default `Vendor`) |
| `CompanyId` | APICONFIG → Company ID (header `CompanyID`) |
| `Tenant` | Tenant name |
| `ClientId` | APICONFIG Client Configuration → Client Name |
| `ClientSecret` | APICONFIG Client Configuration → Client Password |
| `Output:Path` | Local Excel output path (default `vendors.xlsx`) |
| `AzureStorage:AccountName` | Storage account for blob upload (empty = skip upload) |
| `AzureStorage:ContainerName` | Blob container (default `vendors`) |
| `BillDotCom:*` | Bill.com v3 credentials (empty = skip Bill sync) |
| `BillSync:Enabled` | `true`/`false`/omit (omit = run when BillDotCom creds present) |
| `BillSync:AutoApplyExact` | Skip Y/N for exact matches (forced on in Production) |
| `BillSync:AutoApplyRelaxed` | Skip Y/N for relaxed matches (forced on in Production) |

In **Production** (`DOTNET_ENVIRONMENT=Production`), exact updates, relaxed updates, and creates of unmatched M2M vendors all run without prompts. Locally, confirm each step unless the AutoApply* flags are set.

Grant type on the API client must be **CLIENTCREDENTIALS**. The M2M user bound
to that client is configured in APICONFIG (User Name field) — it is not sent in
the token request.

You can override any value with environment variables, e.g. `Aptean__ClientSecret`.

In Azure the scheduled job sets `KeyVault__Name` and loads `Aptean--*` secrets
from Key Vault via `DefaultAzureCredential` (user-assigned managed identity).

## Run

```bash
cd src/ApteanM2MReader
cp appsettings.example.json appsettings.json   # then edit credentials
dotnet run
dotnet run -- --output ../../vendors.xlsx         # optional path override
```

### Endpoints

Token:

`POST {BaseUrl}/idsrvapi/connect/token`

Vendors list (brief projection — City/Company/Street/VendorNumber only):

`GET {BaseUrl}/{ContextPath}/api/{ObjectName}?Page={n}`

Vendor detail (full record, including Zip/State/Phone):

`GET {BaseUrl}/{ContextPath}/api/{ObjectName}/{VendorNumber}`

Example:

`GET https://apps.m2m.apteangovcloud.com/webapi/api/Vendor/3CHEM`

Mapped fields: `VendorNumber` → `Vendor`, `Company`, `StreetAddress` → `Street`, `City`, `State`, `ZipCode` → `Zip Code`, `Country`, `Phone` → `Phone Number`, `EMail` → `Email`.

### Excel columns

| Header | Content |
| --- | --- |
| `Vendor` | M2M vendor ID |
| `Company` | Vendor name |
| `Street` | Street address |
| `City` | City |
| `State` | State |
| `Zip Code` | Zip / postal code |
| `Country` | Country |
| `Phone Number` | Phone |
| `Email` | Email |

`Vendor` / `Company` / `City` / `Zip Code` remain compatible with `Excel/M2MVendorReader`. Extra columns are ignored by that reader when present.
