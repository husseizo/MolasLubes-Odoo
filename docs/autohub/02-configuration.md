# AutoHub — Configuration Shape

## Overview

The single flat `SapSettings` block is replaced by a named-profile structure under `IntegrationProfiles`. The existing `MolasLubes` profile carries the current values verbatim. The new `AutoHub` profile carries the `MOLAS_Live_2021` credentials.

Each profile is fully self-contained: SAP credentials, cache DB connection string, and Neon DB connection string live together under the profile key.

---

## Full `appsettings.json` Shape

```json
{
  "IntegrationProfiles": {
    "Default": "MolasLubes",
    "Profiles": {
      "MolasLubes": {
        "Sap": {
          "Server": "YOUR_SAP_SERVER",
          "CompanyDB": "Molas_Lubes_LTD",
          "UserName": "YOUR_USER",
          "Password": "YOUR_PASSWORD",
          "DbServerType": "MSSQL2016",
          "LicenseServer": "YOUR_LICENSE_SERVER:30000",
          "SLDServer": "YOUR_SLD_SERVER:40000",
          "CustomerSeries": "CSR"
        },
        "ConnectionStrings": {
          "CacheDb": "Server=YOUR_SQL_SERVER;Database=MolasCacheDb;User Id=...;Password=...;",
          "NeonDb": "Host=YOUR_NEON_HOST;Database=YOUR_NEON_DB;Username=...;Password=...;"
        }
      },
      "AutoHub": {
        "Sap": {
          "Server": "YOUR_SAP_SERVER",
          "CompanyDB": "MOLAS_Live_2021",
          "UserName": "YOUR_USER",
          "Password": "YOUR_PASSWORD",
          "DbServerType": "MSSQL2016",
          "LicenseServer": "YOUR_LICENSE_SERVER:30000",
          "SLDServer": "YOUR_SLD_SERVER:40000",
          "CustomerSeries": "CSR"
        },
        "ConnectionStrings": {
          "CacheDb": "Server=YOUR_SQL_SERVER;Database=MOLAS_Live_2021_Cache;User Id=...;Password=...;",
          "NeonDb": "Host=YOUR_NEON_HOST;Database=MolasAutoHub;Username=...;Password=...;"
        }
      }
    }
  },
  "GermaxScraper": {
    "BaseUrl": "https://germaxparts.com",
    "RequestTimeoutSeconds": 30,
    "DelayBetweenRequestsMs": 1500,
    "MaxConcurrency": 1,
    "SearchPaths": [
      "/?s={term}&post_type=product"
    ],
    "AllowedItemGroups": [ "Land Rover", "Volvo" ],
    "SearchStrategyOrder": [
      "item_code",
      "item_name_engine_code",
      "item_name_only"
    ],
    "MaxCandidatesPerSearch": 5
  }
}
```

---

## `appsettings.Development.json` Overrides

Credentials and connection strings are overridden per environment. The development file should **never** be committed with real credentials.

```json
{
  "IntegrationProfiles": {
    "Profiles": {
      "AutoHub": {
        "Sap": {
          "Server": "localhost",
          "CompanyDB": "MOLAS_Live_2021",
          "UserName": "dev_user",
          "Password": "dev_password"
        },
        "ConnectionStrings": {
          "CacheDb": "Server=localhost;Database=MOLAS_Live_2021_Cache_Dev;Trusted_Connection=True;",
          "NeonDb": "Host=localhost;Port=5432;Database=MolasAutoHub_Dev;Username=postgres;Password=dev_password;"
        }
      }
    }
  },
  "GermaxScraper": {
    "DelayBetweenRequestsMs": 500,
    "MaxConcurrency": 1
  }
}
```

---

## Environment Variables (Secrets)

For production, override credentials via environment variables using the `:` → `__` convention:

```
IntegrationProfiles__Profiles__AutoHub__Sap__Password=<secret>
IntegrationProfiles__Profiles__AutoHub__ConnectionStrings__CacheDb=<connection_string>
IntegrationProfiles__Profiles__AutoHub__ConnectionStrings__NeonDb=<connection_string>
```

---

## Options Classes

Bound in `Program.cs` or a dedicated extension:

```csharp
services.Configure<IntegrationProfilesOptions>(
    configuration.GetSection(IntegrationProfilesOptions.SectionName));

services.Configure<GermaxScraperSettings>(
    configuration.GetSection(GermaxScraperSettings.SectionName));
```

Resolve in services via:

```csharp
public class SapCompanyConnectionFactory
{
    public SapCompanyConnectionFactory(IOptions<IntegrationProfilesOptions> opts) { ... }
}
```

---

## Migration Notes

The existing `SapSettings` section in `appsettings.json` remains bound via the existing `SapDiApiConnection` during the backward-compatibility window. Once all existing readers/writers are migrated to the factory, remove the legacy `SapSettings` block.

| Stage | Config state |
|---|---|
| Phase 1–2 | Both `SapSettings` (legacy) and `IntegrationProfiles` coexist |
| Phase 3–5 | `SapSettings` is still present; `MolasLubes` profile mirrors it |
| Phase 6 | Remove legacy `SapSettings`; `IntegrationProfiles.MolasLubes` is the sole source |

---

## GermaxScraper Settings Reference

| Key | Type | Default | Description |
|---|---|---|---|
| `BaseUrl` | string | `https://germaxparts.com` | Root URL for all Germax requests |
| `RequestTimeoutSeconds` | int | `30` | HTTP client timeout per request |
| `DelayBetweenRequestsMs` | int | `1500` | Polite delay between requests |
| `MaxConcurrency` | int | `1` | Parallel scrape workers (keep at 1) |
| `SearchPaths` | string[] | see above | URL templates; `{term}` is replaced by encoded search term |
| `AllowedItemGroups` | string[] | `["Land Rover","Volvo"]` | SAP item group names to include in seed |
| `SearchStrategyOrder` | string[] | see above | Order in which search strategies are attempted |
| `MaxCandidatesPerSearch` | int | `5` | Max candidates evaluated per seed item |
