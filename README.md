# M2M to Bill.com Vendor Account Number Sync

A .NET 8 console application that matches vendors from an M2M Excel export against
vendors in Bill.com and writes each matched `M2MVendorID` into the corresponding
Bill.com vendor's `companyName` field (`additionalInfo.companyName`).

## What it does

1. Reads the M2M vendor list from an Excel (`.xlsx`) file.
2. Signs in to the Bill.com v3 API and fetches **all** vendors (paginated).
3. Matches Bill.com vendors to M2M rows in two passes (see below), using **City**
   and **Zip** as a tiebreaker when several vendors are plausible.
4. Prints a preview table of intended changes (exact vs relaxed) and asks for
   **Y/N** confirmation — **separately** for exact and relaxed matches.
5. On confirmation, updates each matched vendor's `companyName` via
   `PATCH /v3/vendors/{vendorId}` (body `{ "additionalInfo": { "companyName": "..." } }`).
6. Writes a timestamped CSV report under `reports/`.

### Matching rules

Match key: vendor name. `Company` (Excel) ↔ `name` (Bill.com). Two passes run in order:

1. **Exact pass** — names compared after normalization (lowercase, punctuation/
   whitespace collapsed) and stripping legal suffixes (`Inc`, `LLC`, `Corp`, `Co`,
   `Ltd`, ...).
2. **Relaxed pass** (only for rows unmatched after the exact pass) — names reduced
   to their **significant tokens** by additionally dropping a leading article
   (`The`), web TLDs (`.com`, `.net`, ...), and generic noise words (`group`,
   `supply`/`supplies`, `vendor`, `sales`, `service(s)`, `usa`, `holdings`,
   `enterprises`, `company`, `of`, `and`). Tokens match when the two sets are
   **equal** or one is a **subset** of the other. Example resolutions:
   `SOLIDEXPERTS` ↔ `The SolidExperts`, `ETRAILER` ↔ `ETrailer.com`,
   `CRATERS & FREIGHTERS OF ORLANDO` ↔ `Craters & Freighters`.

Common rules for both passes:

- Tiebreaker: `City` and `Zip Code` (Excel) ↔ `address.city` / `address.zipOrPostalCode`.
- A non-empty existing `companyName` is **never overwritten** — flagged as `AlreadySet`.
- Multiple unresolved candidates → `Ambiguous` (skipped, reported).
- No match → `Unmatched` (reported).
- Relaxed matches require the tiebreaker to single out a unique vendor when more
  than one candidate remains, and are confirmed with their own separate Y/N prompt.

### Incomplete-address vendors

Bill.com re-validates a vendor's whole record on update, and vendors payable by
check require a complete address (`line1`, `city`, `zipOrPostalCode`, `country`).
If a vendor's stored address is missing `city`/`zip`, the `companyName`-only PATCH
fails with `address.city: must not be blank` (and/or `address.zipOrPostalCode`).

The tool handles this automatically:

1. It first sends `companyName` only.
2. If that fails with the incomplete-address error, it retries **once**, supplying
   the missing `city`/`zip` from the matched M2M Excel row (preserving the
   vendor's existing `line1`/`state`/`country`).
3. If the Excel row also lacks `city`/`zip`, the vendor is **skipped** and flagged
   in the report (`Applied=false` with an explanatory `Error`).

Successful address-fill updates are logged with an `[OK*]` marker.

## Excel format

Columns are located by their **header text**, so column order does not matter.
Required headers:

| Header    | Maps to                                   |
| --------- | ----------------------------------------- |
| `Vendor`  | M2MVendorID (written to `companyName`)    |
| `Company` | Vendor name (match key)                   |

Optional headers used as tiebreakers: `City`, `Zip Code` (aliases: `Zip`, `ZipCode`).

## Configuration

Edit [appsettings.json](appsettings.json):

```json
{
  "BillDotCom": {
    "BaseUrl": "https://gateway.stage.bill.com/connect/v3/",
    "DevKey": "<dev key>",
    "OrganizationId": "<org id>",
    "Username": "<username>",
    "Password": "<password>"
  },
  "Excel": {
    "Path": "vendors.xlsx",
    "SheetName": ""
  }
}
```

> Security note: credentials are stored in `appsettings.json` for convenience in
> the sandbox. For real runs, prefer environment variables or
> [user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets)
> instead of committing secrets. Environment variables override the JSON file,
> e.g. `BillDotCom__Password=...`.

## Running

```bash
dotnet run                       # uses Excel:Path from appsettings.json
dotnet run -- --excel data.xlsx  # override the Excel path
```

The app prints a preview, then prompts (relaxed matches only prompt if any exist):

```
Apply N EXACT update(s)? (Y/N):
...
Apply M RELAXED update(s)? (Y/N):
```

Type `Y` to apply each group, anything else to skip it. A CSV report is always written.

## Output

- Console: preview table, per-vendor apply results, and a final summary.
- `reports/sync-report-<timestamp>.csv`: full results for every row, including
  `Status`, `MatchType`, `VendorName`, `M2MVendorName`, `BillVendorId`,
  `M2MVendorID`, `CurrentCompanyName`, `Applied`, `Error`, and `Note`.

## Project layout

```
Program.cs                     Orchestration (load, match, preview, confirm, apply)
Config/AppSettings.cs          Strongly-typed configuration
BillDotCom/BillClient.cs       Bill.com v3 API client (login, list, update, logout)
BillDotCom/BillDtos.cs         Request/response DTOs
Excel/M2MVendorReader.cs       Reads the M2M Excel export
Excel/M2MVendor.cs             M2M row model
Matching/VendorMatcher.cs      Name normalization + City/Zip tiebreaker
Matching/MatchResult.cs        Match outcome model
Reporting/ReportWriter.cs      Console preview/summary + CSV report
```
