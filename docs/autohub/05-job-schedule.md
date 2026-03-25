# AutoHub — Job Schedule Design

## Overview

Profile B uses four Quartz jobs. They are all scoped to the AutoHub profile and do not interact with Profile A jobs or databases.

---

## Job 1 — SAP Seed Sync

| Property | Value |
|---|---|
| **Class** | `AutoHubSapSeedSyncJob` |
| **File** | `src/MolasLubes.Infrastructure/Scheduling/Jobs/AutoHubSapSeedSyncJob.cs` |
| **Schedule** | Every 6 hours |
| **Cron** | `0 0 */6 ? * *` |

### Responsibilities

1. Connect to `MOLAS_Live_2021` via the AutoHub `SapCompanyProfile`
2. Run the seed query (`OITM JOIN OITB` for Land Rover / Volvo)
3. Use delta watermark on subsequent runs to limit SAP load
4. Upsert rows into `CacheGermaxProducts` (`MOLAS_Live_2021_Cache`)
5. Set `LastSapSeedAt` on every touched row
6. Set `IsActive = false` for items that SAP returns with `frozenFor = 'Y'`

### Execution

```
MOLAS_Live_2021 (SAP)
       │  SapAutoHubSeedReader
       ▼
CacheGermaxProducts (SQL Server)
  ← upsert seed fields only
  ← do NOT touch ScrapeStatus if already SCRAPED
```

---

## Job 2 — Germax Enrichment

| Property | Value |
|---|---|
| **Class** | `GermaxProductEnrichmentJob` |
| **File** | `src/MolasLubes.Infrastructure/Scheduling/Jobs/GermaxProductEnrichmentJob.cs` |
| **Schedule** | Nightly |
| **Cron** | `0 30 1 ? * *` (01:30 UTC) |

### Responsibilities

1. Load batch of `CacheGermaxProducts` rows where `ScrapeStatus` is null, `PENDING`, or recently updated
2. For each row, call `GermaxProductScraperService`
3. Persist enriched data back to `CacheGermaxProducts` via `GermaxCacheSyncService`
4. Replicate enriched rows to `MolasAutoHub` (Neon) via `GermaxAutoHubSyncService`

### Execution Order (within one job run)

```
1. Load pending rows from CacheGermaxProducts
2. GermaxProductScraperService.SearchCandidatesAsync()
3. GermaxProductScraperService.ResolveBestCandidateAsync()
4. GermaxProductScraperService.ScrapeProductPageAsync()
5. GermaxCacheSyncService.UpsertAsync()         → MOLAS_Live_2021_Cache
6. GermaxAutoHubSyncService.UpsertAsync()       → MolasAutoHub (Neon)
```

### Constraint

Do not run enrichment and seed sync at the same time for the same profile. Use Quartz job chaining or a named lock to enforce this.

---

## Job 3 — Retry Failed Scrapes

| Property | Value |
|---|---|
| **Class** | `GermaxRetryFailedJob` |
| **File** | `src/MolasLubes.Infrastructure/Scheduling/Jobs/GermaxRetryFailedJob.cs` |
| **Schedule** | Every 12 hours |
| **Cron** | `0 0 */12 ? * *` |

### Responsibilities

1. Load rows with `ScrapeStatus = 'ERROR'` or `ScrapeStatus = 'NO_MATCH'`
2. Limit to recent failures (e.g. failed in the last 7 days)
3. Cap batch size to avoid excessive website traffic
4. Re-attempt enrichment using same pipeline as Job 2
5. If still failing after N attempts, leave in ERROR and stop retrying until manual review

---

## Job 4 — Manual Admin Triggers

No scheduled cron. Triggered via HTTP POST endpoints.

| Endpoint | Job invoked | Description |
|---|---|---|
| `POST /api/admin/autohub/seed-sync` | `AutoHubSapSeedSyncJob` | Force immediate SAP seed sync |
| `POST /api/admin/autohub/germax/scrape` | `GermaxProductEnrichmentJob` | Force immediate enrichment run |
| `POST /api/admin/autohub/germax/retry-failed` | `GermaxRetryFailedJob` | Force immediate retry of failed rows |

All admin endpoints require the existing API key authentication (`ApiKeyAttribute`).

---

## Full Schedule Summary

| Job | Cron | Approximate UTC time |
|---|---|---|
| `AutoHubSapSeedSyncJob` | `0 0 */6 ? * *` | 00:00, 06:00, 12:00, 18:00 |
| `GermaxProductEnrichmentJob` | `0 30 1 ? * *` | 01:30 |
| `GermaxRetryFailedJob` | `0 0 */12 ? * *` | 00:00, 12:00 |

---

## Quartz Registration

```csharp
// In Program.cs or AddAutoHubJobs() extension method

services.AddQuartz(q =>
{
    // Job 1 — SAP seed sync
    var seedJobKey = new JobKey("AutoHubSapSeedSyncJob");
    q.AddJob<AutoHubSapSeedSyncJob>(opts => opts.WithIdentity(seedJobKey));
    q.AddTrigger(opts => opts
        .ForJob(seedJobKey)
        .WithIdentity("AutoHubSapSeedSyncJob-trigger")
        .WithCronSchedule("0 0 */6 ? * *"));

    // Job 2 — Germax enrichment
    var enrichJobKey = new JobKey("GermaxProductEnrichmentJob");
    q.AddJob<GermaxProductEnrichmentJob>(opts => opts.WithIdentity(enrichJobKey));
    q.AddTrigger(opts => opts
        .ForJob(enrichJobKey)
        .WithIdentity("GermaxProductEnrichmentJob-trigger")
        .WithCronSchedule("0 30 1 ? * *"));

    // Job 3 — Retry failed
    var retryJobKey = new JobKey("GermaxRetryFailedJob");
    q.AddJob<GermaxRetryFailedJob>(opts => opts.WithIdentity(retryJobKey));
    q.AddTrigger(opts => opts
        .ForJob(retryJobKey)
        .WithIdentity("GermaxRetryFailedJob-trigger")
        .WithCronSchedule("0 0 */12 ? * *"));
});
```

---

## Profile A / Profile B Job Isolation

Profile A jobs (`ProductFullSyncJob`, `NeonProductDeltaSyncJob`, etc.) must never reference `Live2021CacheDbContext` or `AutoHubDbContext`. Profile B jobs must never reference `MolasCacheDbContext` or `NeonDbContext`.

Enforce this at registration time by injecting the correct context via constructor — the DI container will enforce the boundary automatically as long as contexts are registered separately.

---

## Rollout Sequence

```
Week 1  Add config + DB contexts + migrations
Week 2  SapAutoHubSeedReader + AutoHubSapSeedSyncJob
Week 3  GermaxProductScraperService (Land Rover proof of concept)
Week 4  GermaxProductEnrichmentJob + GermaxCacheSyncService + GermaxAutoHubSyncService
Week 5  GermaxRetryFailedJob + NO_MATCH workflow + admin endpoints
Week 6+ Volvo expansion (only after match-rate review)
```
