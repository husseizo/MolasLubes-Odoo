# Configuration Guide

This document describes all required configuration keys for the MolasLubes API,
and explains how to supply secrets securely for local development and production.

---

## ⚠️ Credential Rotation Notice

The following credentials were previously committed to this repository and **must
be considered compromised**. Rotate them immediately:

| Credential | Action required |
|---|---|
| SQL Server `MolasCacheDb` password | Rotate the `sa` (or service account) password |
| Neon PostgreSQL password | Rotate via the Neon dashboard |
| SAP B1 service user password | Rotate in SAP B1 user management |
| Odoo API key | Revoke and regenerate in Odoo → Settings → Technical → API Keys |
| Application `ApiSecurity:ApiKey` | Generate a new random key |

---

## Configuration Sections

### `ConnectionStrings`

| Key | Description |
|---|---|
| `MolasCacheDb` | ADO.NET connection string for the local SQL Server cache database. |
| `NeonDb` | Npgsql connection string for the Neon PostgreSQL database. |

**Example (trusted local SQL Server):**
```json
"ConnectionStrings": {
  "MolasCacheDb": "Server=.;Database=MolasCacheDb;Trusted_Connection=True;TrustServerCertificate=True",
  "NeonDb": "Host=<host>;Database=<db>;Username=<user>;Password=<password>;SSL Mode=Require"
}
```

---

### `SAP`

SAP B1 DiApi connection settings. **Windows only** (requires the SAPbobsCOM COM reference).

| Key | Description |
|---|---|
| `Server` | Hostname of the SAP B1 server. |
| `CompanyDB` | SAP company database name. |
| `UserName` | SAP B1 service user login. |
| `Password` | SAP B1 service user password. **Never commit.** |
| `DbServerType` | e.g. `MSSQL2016`. |
| `LicenseServer` | `<host>:30000` |
| `SLDServer` | `<host>:40000` |

---

### `ApiSecurity`

| Key | Description |
|---|---|
| `ApiKey` | The shared API key required for protected admin endpoints (`X-API-KEY` header). |

---

### `OdooApi`

| Key | Description |
|---|---|
| `BaseUrl` | Root URL of the Odoo instance (no trailing slash). |
| `ApiKey` | Odoo API key. **Never commit.** |

---

### `SyncSettings`

Feature flags controlling which sync pipelines are active at startup.

| Key | Type | Default | Description |
|---|---|---|---|
| `EnableInvoiceCacheSync` | bool | `true` | SAP → Cache invoice sync. |
| `EnablePaymentCacheSync` | bool | `true` | SAP → Cache payment sync. |
| `EnableNeonInvoiceSync` | bool | `true` | Cache → Neon invoice sync. |
| `EnableNeonPaymentSync` | bool | `true` | Cache → Neon payment sync. |
| `EnableNeonCustomerSync` | bool | `true` | Cache → Neon customer sync. |
| `EnableOdooDeliveryPush` | bool | `false` | Push deliveries to Odoo. |
| `EnableOdooInvoicePush` | bool | `false` | Push invoices to Odoo. |
| `EnableOdooPaymentPush` | bool | `false` | Push payments to Odoo. |

---

### `LiquiMolyScraper`

| Key | Default | Description |
|---|---|---|
| `BaseUrl` | `https://www.liqui-moly.com` | Root URL for Liqui-Moly website (no trailing slash). |
| `OwwApiPrefix` | `""` | Optional hard-coded OWW API prefix. Leave empty to auto-detect. |
| `DelayBetweenRequestsMs` | `1500` | Milliseconds between page requests (polite crawling). |
| `DelayBetweenCategoriesMs` | `3000` | Milliseconds between category-level pauses. |
| `RequestTimeoutSeconds` | `30` | HTTP client timeout in seconds. |
| `BatchSize` | `50` | Article numbers to scrape per batch before saving. |
| `MaxConcurrency` | `1` | Max concurrent HTTP requests during detail-page enrichment. Keep at `1` to avoid bot detection. |
| `MaxParallelRequests` | `6` | Max parallel requests for category listing phase. |

---

## Supplying Secrets Securely

### Option 1 — .NET User Secrets (recommended for local development)

User Secrets are stored outside the repository in your OS user profile and are
**never committed to git**.

```bash
cd src/MolasLubes.Api

# Initialize (only needed once per machine; UserSecretsId is already in the .csproj)
dotnet user-secrets init

# Set individual secrets
dotnet user-secrets set "ConnectionStrings:MolasCacheDb" "Server=.;Database=MolasCacheDb;User Id=sa;Password=<your-password>;TrustServerCertificate=True"
dotnet user-secrets set "ConnectionStrings:NeonDb" "Host=<host>;Database=<db>;Username=<user>;Password=<password>;SSL Mode=Require"
dotnet user-secrets set "SAP:Password" "<your-sap-password>"
dotnet user-secrets set "OdooApi:ApiKey" "<your-odoo-api-key>"
dotnet user-secrets set "ApiSecurity:ApiKey" "<your-generated-api-key>"
```

User Secrets override `appsettings.Development.json` when running with
`ASPNETCORE_ENVIRONMENT=Development`.

### Option 2 — Local override file (alternative for local development)

Create `src/MolasLubes.Api/appsettings.Development.local.json` (already in
`.gitignore`) and place only your secret overrides there:

```json
{
  "ConnectionStrings": {
    "MolasCacheDb": "Server=.;Database=MolasCacheDb;User Id=sa;Password=<your-password>;TrustServerCertificate=True",
    "NeonDb": "Host=<host>;Database=<db>;Username=<user>;Password=<password>;SSL Mode=Require"
  },
  "SAP": {
    "Password": "<your-sap-password>"
  },
  "OdooApi": {
    "ApiKey": "<your-odoo-api-key>"
  },
  "ApiSecurity": {
    "ApiKey": "<your-generated-api-key>"
  }
}
```

> **Important:** `appsettings.*.local.json` is listed in `.gitignore`. Confirm
> the file does not appear in `git status` before committing.

### Option 3 — Environment variables (CI/CD and production)

ASP.NET Core maps environment variables to configuration using `__` as the section
separator. Set these in your deployment environment (e.g. GitHub Actions secrets,
Azure App Service application settings, Docker `-e` flags):

```bash
ConnectionStrings__MolasCacheDb="Server=...;Password=<secret>"
ConnectionStrings__NeonDb="Host=...;Password=<secret>"
SAP__Password="<secret>"
OdooApi__ApiKey="<secret>"
ApiSecurity__ApiKey="<secret>"
```

---

## Validating the Configuration

After setup, run the application and confirm it starts without errors:

```bash
cd src/MolasLubes.Api
dotnet run
```

A successful start will log:

```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
```

Any `System.IO.InvalidDataException: Failed to load configuration` errors indicate
malformed JSON — validate both `appsettings.json` and `appsettings.Development.json`
with a JSON linter before re-running.
