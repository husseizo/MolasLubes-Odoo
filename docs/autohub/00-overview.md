# AutoHub Integration Profile — Architecture Overview

## Target Shape

This document describes **Profile B: AutoHub**, a second isolated integration profile built alongside (not inside) the existing Profile A.

## Dual-Profile Topology

```
Profile A (existing — DO NOT MODIFY)
  Molas_Lubes_LTD  →  MolasCacheDb (SQL Server)  →  NeonDb (PostgreSQL)

Profile B (new — this spec)
  MOLAS_Live_2021  →  MOLAS_Live_2021_Cache (SQL Server)  →  MolasAutoHub (Neon/PostgreSQL)
                                                                      ↑
                                                            Germax enrichment
```

Each profile has its own:
- SAP company connection credentials
- Cache SQL Server database
- Neon/PostgreSQL database
- Scheduled jobs
- DB contexts (no shared EF registrations)
- Admin API endpoints

This isolation ensures **zero cross-company contamination** and keeps the current production system fully stable.

---

## Why Isolated Profiles

| Concern | Single-profile risk | Profile isolation solution |
|---|---|---|
| SAP company data bleed | Readers/writers connect to wrong company | Each profile has its own `SapCompanyProfile` and factory |
| EF context sharing | Migrations or config leaks between DBs | Dedicated `Live2021CacheDbContext` and `AutoHubDbContext` |
| Job scheduling conflict | Cache jobs overwrite wrong DB rows | Jobs are profile-scoped; can't run cross-profile |
| Scraper noise | Germax traffic attributed to wrong items | Seed reader only queries `MOLAS_Live_2021` |

---

## Data Flow — Profile B

```
MOLAS_Live_2021 (SAP)
        │
        │ SapAutoHubSeedReader
        │ (OITM + OITB — Land Rover / Volvo)
        ▼
MOLAS_Live_2021_Cache (SQL Server)
  table: CacheGermaxProducts
        │
        │ GermaxProductEnrichmentJob
        │     └── GermaxProductScraperService
        │           (germaxparts.com)
        ▼
MOLAS_Live_2021_Cache (SQL Server)
  table: CacheGermaxProducts (enriched columns filled)
        │
        │ GermaxAutoHubSyncService
        ▼
MolasAutoHub (Neon / PostgreSQL)
  table: NeonGermaxProducts
```

---

## Layer Responsibilities

| Layer | Component | Responsibility |
|---|---|---|
| SAP seed | `SapAutoHubSeedReader` | Pull candidate items from SAP MOLAS_Live_2021 |
| Cache write | `GermaxCacheSyncService` | Upsert seed + scraped data into SQL Server cache |
| Enrichment | `GermaxProductScraperService` | Scrape germaxparts.com for matched product data |
| Neon sync | `GermaxAutoHubSyncService` | Replicate enriched rows to MolasAutoHub PostgreSQL |
| Orchestration | `GermaxProductEnrichmentJob` | Drive the full enrichment pipeline |
| Admin API | `AutoHubGermaxProductsController` | Read endpoints + manual trigger endpoints |

---

## Rollout Phases

| Phase | Scope |
|---|---|
| 1 | Profile-based config + second DB contexts |
| 2 | `SapAutoHubSeedReader` + seed table |
| 3 | Germax scraper proof of concept — Land Rover only |
| 4 | Persist to `MOLAS_Live_2021_Cache` and `MolasAutoHub` |
| 5 | Retry / NO_MATCH workflow |
| 6 | Expand to Volvo — only after real match-rate validation |

> **Note:** Volvo support on germaxparts.com is not confirmed. In Phase 1–5, Volvo items are seeded and stored with `ScrapeStatus = 'NO_MATCH'`. Review 50–100 samples manually before expanding the matching logic.
