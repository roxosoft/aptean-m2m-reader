# Aptean M2M Web API Reader

A .NET 8 console app that authenticates to the Aptean Made2Manage Web API,
fetches vendors, and writes a Bill.com-sync-compatible Excel file.

## Scope

1. Obtain an OAuth access token via **Client Credentials**.
2. `GET` all vendors from the M2M Web API (paged list), then enrich each with a detail `GET`.
3. Write `vendors.xlsx` with headers expected by `JetSolutions.BillVendorSync` (plus State / Phone).

## Credentials

Copy `appsettings.example.json` to `appsettings.json` (gitignored) and fill in
values from Made2Manage **APICLIENT** / **APICONFIG**:

| Setting | Source |
| --- | --- |
| `BaseUrl` | M2M site host, e.g. `https://apps.m2m.apteangovcloud.com` |
| `ContextPath` | API context path (govcloud default `webapi`) |
| `ObjectName` | APICONFIG object name (default `Vendor`) |
| `CompanyId` | APICONFIG → Company ID (header `CompanyID`) |
| `Tenant` | Tenant name |
| `ClientId` | APICONFIG Client Configuration → Client Name |
| `ClientSecret` | APICONFIG Client Configuration → Client Password |
| `Output:Path` | Excel output path (default `../vendors.xlsx`) |

Grant type on the API client must be **CLIENTCREDENTIALS**. The M2M user bound
to that client is configured in APICONFIG (User Name field) — it is not sent in
the token request.

You can override any value with environment variables, e.g. `Aptean__ClientSecret`.

## Run

```bash
cd ApteanM2MReader
cp appsettings.example.json appsettings.json   # then edit credentials
dotnet run
dotnet run -- --output ../vendors.xlsx         # optional path override
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

Mapped fields: `VendorNumber` → `Vendor`, `Company`, `City`, `State`, `ZipCode` → `Zip Code`, `Phone` → `Phone Number`.

### Excel columns

| Header | Content |
| --- | --- |
| `Vendor` | M2M vendor ID |
| `Company` | Vendor name |
| `City` | City |
| `State` | State |
| `Zip Code` | Zip / postal code |
| `Phone Number` | Phone |

`Vendor` / `Company` / `City` / `Zip Code` match `Excel/M2MVendorReader` in the Bill.com sync project. Extra columns are ignored by that reader.
