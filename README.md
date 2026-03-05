# MolasLubes

A C# / ASP.NET Core API that bridges SAP B1 → local SQL Server cache → Neon PostgreSQL → Odoo.

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Data Flow](#data-flow)
3. [Liqui-Moly Product Cache Enrichment](#liqui-moly-product-cache-enrichment)
   - [How it works](#how-it-works)
   - [Configuration](#configuration)
   - [Running the enrichment job](#running-the-enrichment-job)
   - [API endpoints](#api-endpoints)
4. [Admin Endpoints](#admin-endpoints)
5. [Development Setup](#development-setup)

---

## Architecture Overview

```
SAP B1 (DiApi)
    │
    ▼
MolasCacheDb (SQL Server)   ←── Liqui-Moly scraper enrichment
    │
    ▼
NeonDb (PostgreSQL)          ←── Liqui-Moly scraper enrichment
    │
    ▼
Odoo
```

---

## Data Flow

| Layer | Source | Destination | Services |
|-------|--------|-------------|----------|
| 1 | SAP B1 | Cache DB (SQL Server) | `SapProductReader` → `ProductCacheService` |
| 2 | Cache DB | Neon DB (PostgreSQL) | `ProductNeonSyncService` |
| 3 | Neon DB | Odoo | `OdooDeliveryPushService` etc. |
| ∞ | liqui-moly.com | Cache + Neon | `LiquiMolyProductScrapeJob` |

---

## Liqui-Moly Product Cache Enrichment

### How it works

The `LiquiMolyProductScrapeJob` Quartz job runs the full enrichment pipeline:

1. **Reads** all distinct active `ItemCode` values from `CacheProducts` (SQL Server).  
   Each `ItemCode` is treated as a Liqui-Moly article number (e.g. `"20001"`).

2. **Splits** the article numbers into configurable batches (`BatchSize`, default 50).

3. For each batch, calls **`LiquiMolyProductScraperService.ScrapeByArticleNumbersAsync`**:
   - *Phase 1* — per-article Magento 2 catalog search (`/en/catalogsearch/result/?q=<sku>`)
     to find the product URL without hitting bot-protected category listing pages.
   - *Phase 2* — detail-page enrichment: visits the product URL, extracts name,
     description, spec grade, packaging sizes, images, approvals, specs table, and
     PDF download links.

4. **Upserts** the scraped data into:
   - `CacheLiquiMolyProducts` (SQL Server) via `LiquiMolyCacheSyncService`
   - `NeonLiquiMolyProducts` (PostgreSQL) via `LiquiMolyNeonSyncService`

5. After all batches complete, **marks stale products inactive** in both stores
   (products whose article number was not returned by the scraper in this run).

Batches are saved incrementally so partial progress is not lost if the job is
cancelled or fails midway.

### Configuration

Add or update the `LiquiMolyScraper` section in `appsettings.json`
(or `appsettings.Development.json` for local development):

```json
"LiquiMolyScraper": {
  "BaseUrl": "https://www.liqui-moly.com",
  "DelayBetweenRequestsMs": 1500,
  "DelayBetweenCategoriesMs": 3000,
  "RequestTimeoutSeconds": 30,
  "BatchSize": 50,
  "MaxConcurrency": 1,
  "OwwApiPrefix": ""
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `BaseUrl` | `https://www.liqui-moly.com` | Root URL for Liqui-Moly website (no trailing slash). |
| `DelayBetweenRequestsMs` | `1500` | Milliseconds between page requests (polite crawling). |
| `DelayBetweenCategoriesMs` | `3000` | Milliseconds between category-level pauses. |
| `RequestTimeoutSeconds` | `30` | HTTP request timeout in seconds. |
| `BatchSize` | `50` | Article numbers to scrape per batch before saving. Smaller values reduce peak memory and allow incremental saves. |
| `MaxConcurrency` | `1` | Max parallel HTTP requests during detail-page enrichment. Keep at `1` (sequential) to avoid bot detection. |
| `OwwApiPrefix` | `""` | Optional hard-coded OWW API prefix for the oil-guide fallback. Leave empty to auto-detect. |

### Running the enrichment job

**Scheduled:** The job runs automatically once daily at **02:00 UTC** (configured in `Program.cs`).

**Manual trigger via HTTP:**

```bash
# Requires the API key header
curl -X POST https://<host>/api/admin/liquimoly/scrape \
     -H "X-API-KEY: <your-api-key>"
```

The response is immediate (`202 Accepted`-style); the job runs asynchronously in the
Quartz scheduler background thread.

### API endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET`  | `/api/liquimoly/products` | List scraped products (supports `?search=`, `?category=`, `?activeOnly=`, `?take=`). |
| `GET`  | `/api/liquimoly/products/{articleNumber}` | Get full detail for one product including all JSON fields deserialised. |
| `GET`  | `/api/liquimoly/products/categories` | List distinct active categories. |
| `POST` | `/api/admin/liquimoly/scrape` | Manually trigger the scrape job (protected by API key). |

---

## Admin Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/admin/sync/products/full` | Full SAP → Cache product sync. |
| `POST` | `/api/admin/neon-sync/products` | Cache → Neon product delta sync. |
| `POST` | `/api/admin/liquimoly/scrape` | Trigger Liqui-Moly scrape job. |

---

## Development Setup

### Prerequisites

- .NET 10 SDK
- SQL Server (local or Docker) — connection string: `MolasCacheDb`
- PostgreSQL / Neon — connection string: `NeonDb`
- **Windows only** for the SAP B1 DiApi COM reference (SAPbobsCOM)

### Running locally

```bash
cd src/MolasLubes.Api
dotnet run
```

Database migrations run automatically on startup (EF Core `Database.Migrate()`).

### Running tests

```bash
dotnet test tests/MolasLubes.Tests/MolasLubes.Tests.csproj
```

> **Note:** The test project only references `MolasLubes.Domain` to avoid the
> Windows-only COM dependency in `MolasLubes.Infrastructure`.  Unit tests cover
> entity defaults, JSON field round-trips, mapping logic, and batch-split arithmetic.
