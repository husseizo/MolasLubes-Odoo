# MolasLubes Tier Roadmap: Tranche Path to Full Separation

**Purpose**: Define the 4-tier progression from single-project monolith → distributed 3-service architecture, with clear launch criteria and rollback points.

---

## High-Level Progression

```
CURRENT (Tier A): Single MolasLubes.Api
├─ Api handles HTTP requests
├─ Api writes Cache (sync from SAP)
├─ Background jobs run inside Api host (not ideal)
└─ No scheduled syncs to Neon yet

        ↓ SPLIT (Tier B → Tier C)

TIER B: Create SyncWorker, keep as DLL, run in-process
├─ SyncWorker jobs execute inside Api host
├─ Neon syncs begin (reads from Cache, writes to Neon, pushes Odoo)
├─ Separation is logical (different namespaces) not physical (same process)
└─ Low risk: only adds functionality, no cross-talk yet

        ↓ SPLIT (Tier C → Tier C+)

TIER C: Physical Split - SyncWorker → separate exe + Quartz scheduler
├─ SyncWorker runs as independent console app or Windows service
├─ Api continues (now simplified: HTTP + SAP + Cache)
├─ ScraperWorker runs as separate exe (enrichment jobs)
├─ Three independent processes, shared databases
├─ Quartz clustering (IJobStore in Neon detects overlaps)
└─ Higher observability: can monitor/restart each service independently

        ↓ SCALE (Tier C → Tier D)

TIER D: Advanced Operational Features
├─ Independent throttling for CRITICAL/OPTIONAL sync lanes
├─ Automatic pause/resume based on Neon load
├─ Distributed caching (Redis for hot paths)
├─ Per-lane SLAs and alerting
└─ Ready for multi-region deployment (future future)
```

---

## Tier A (Current State): Monolithic Api

### Architecture
- Single `MolasLubes.sln` with only `MolasLubes.Api` project
- Background jobs (if any) run inside Api host on same thread pool
- Neon writes not yet live (only Cache writes from Api)
- No job scheduling database

### Characteristics
| Aspect | Status |
|--------|--------|
| **Scalability** | Single process; can't parallelize background work |
| **Reliability** | Background job failure can crash API responses |
| **Observability** | Single log stream; hard to debug specific job failures |
| **CRITICAL lane** | All endpoints on same process; resource contention |
| **Neon sync** | Not yet active |
| **Odoo push** | Not yet active |

### Exit Criteria (To Advance to Tier B)
- [ ] Create `MolasLubes.SyncWorker` project (DLL + Program.cs for now)
- [ ] Implement 4 initial sync jobs: InvoiceSync, PaymentSync, DeliverySync, CustomerSync
- [ ] Implement OdooXxxPushJob jobs (4 total)
- [ ] Verify Neon connection + migrations work
- [ ] Create `OWNERSHIP_MATRIX.md` to define service boundaries
- [ ] Write 10 integration tests for each job (payload validation, state machines)
- [ ] Load test: 100 concurrent Cache reads during a sync job (verify no connection pool exhaustion)

---

## Tier B: SyncWorker as In-Process DLL

### Architecture
```
Single Api.exe Process:
┌──────────────────────────────────────────┐
│  MolasLubes.Api                          │
│  ┌────────────────────────────────────┐  │
│  │ HTTP Request Handlers              │  │
│  │ (Controllers)                      │  │
│  └────────────────────────────────────┘  │
│  ┌────────────────────────────────────┐  │
│  │ SyncWorker Jobs (Tier B)           │  │
│  │ (Quartz + in-process) ← NEW        │  │
│  │ - InvoiceSyncJob                   │  │ 
│  │ - PaymentSyncJob                   │  │
│  │ - DeliverySyncJob                  │  │
│  │ - NeonInvoicePushJob               │  │
│  │ - ... (8 jobs)                     │  │
│  └────────────────────────────────────┘  │
│  Shared DbContexts:                      │
│  - MolasCacheDb                          │
│  - NeonDb ← NEW                          │
└──────────────────────────────────────────┘
```

### Key Changes
- `MolasLubes.SyncWorker` created as new DLL (referenced by Api)
- Quartz scheduler added to DI in Api
- Neon connection pool initialized (via `NeonDb` DbContext)
- Sync jobs registered: `services.AddTransient<NeonInvoiceSyncJob>()`
- First 3-layer syncs: Cache → Neon → Odoo

### Jobs Implemented (8 Total)

**CRITICAL Tier**:
1. `InvoiceSyncJob` — SAP→Cache (Api), Cache→Neon (Worker), Neon→Odoo (Worker)
2. `PaymentSyncJob` — Same 3-layer pattern
3. `DeliverySyncJob` — Same 3-layer pattern
4. `NeonInvoicePushJob` — Neon→Odoo push
5. `NeonPaymentPushJob` — Neon→Odoo push
6. `NeonDeliveryPushJob` — Neon→Odoo push

**IMPORTANT Tier**:
7. `CustomerSyncJob` — Cache→Neon (customer master data)
8. `SalesOrderSyncJob` — Cache→Neon (order master data)

### Deployment
- Single `docker-compose.yml` with one `molaslubes-api:latest` service
- Quartz uses `RAMJobStore` (in-memory; jobs lost on restart) **← ACCEPTABLE FOR Tier B**
- No scheduler database yet

### Success Criteria

| Metric | Target | How to Verify |
|--------|--------|---|
| **No Neon locks during peak API load** | 0 deadlocks | Load test: 100 concurrent POST /invoices while InvoiceSyncJob runs |
| **First sync within 5 minutes of Api startup** | < 5min | Measure: app startup → first Neon write timestamp |
| **All 8 jobs fire on schedule** | 100% success rate over 24h | Monitor: Quartz job logs, verify timestamps |
| **Cache→Neon payload matches** | ≥99% | Query: sample 100 invoices, compare Cache.Amount = Neon.Amount |
| **Neon→Odoo payload valid** | ≥99% | Odoo API acceptance test: POST receipt with Neon payload |
| **Zero transactional deadlocks** | 0 errors | Monitor: Neon slow query log, deadlock alerts |

### Exit Criteria (To Advance to Tier C)
- [ ] All 8 sync jobs green for 7 consecutive days
- [ ] Load test passed: API latency stable during peak sync load
- [ ] Neon reads averaging <200ms / writes <500ms (within budget)
- [ ] Odoo pushes consistently successful (no malformed payloads)
- [ ] DI container correctly validates ownership (no Api→NeonSyncService access)
- [ ] Integration test suite ≥40 tests, all passing
- [ ] SyncWorker.csproj can be compiled standalone (no circular refs)

---

## Tier C: Physical Service Split (3 Processes)

### Architecture

```
THREE INDEPENDENT PROCESSES:

┌──────────────────────────────┐
│   MolasLubes.Api.exe         │
│   ─────────────────────────  │
│   - HTTP Handlers            │
│   - SAP read/write via COM   │
│   - Cache read/write         │
│   - Port 5000                │
│   Process: Api-{pid}         │
└──────────────────────────────┘

┌──────────────────────────────┐
│   MolasLubes.SyncWorker.exe  │
│   ─────────────────────────  │
│   - Quartz scheduler (DB)    │
│   - 8 sync jobs              │
│   - Cache reader             │
│   - Neon writer              │
│   - Odoo API caller          │
│   No HTTP listener           │
│   Process: SyncWorker-{pid}  │
└──────────────────────────────┘

┌──────────────────────────────┐
│   MolasLubes.Scraper.exe     │
│   ─────────────────────────  │
│   - Quartz scheduler (DB)    │
│   - 4 scraper jobs           │
│   - LiquiMoly/Meguin/Germax  │
│   - Cache enrich writer      │
│   - Neon enrich writer       │
│   No HTTP listener           │
│   Process: Scraper-{pid}     │
└──────────────────────────────┘

        ↓ SHARED DATABASES

┌──────────────────────────────┐
│   MolasCacheDb (SQL Server)  │
│   - Invoices, Payments, ...  │
│   - Cache (ephemeral)        │
└──────────────────────────────┘

┌──────────────────────────────┐
│   NeonDb (PostgreSQL)        │
│   - Invoices, Payments, ...  │
│   - Master data (persistent) │
│   - Quartz job metadata      │
└──────────────────────────────┘
```

### Key Changes from Tier B

1. **New Project**: `MolasLubes.SyncWorker.csproj` (no longer DLL reference, separate exe)
2. **New Project**: `MolasLubes.ScraperWorker.csproj` (enrichment jobs)
3. **Quartz Now Uses Database**: `IJobStore → JobStoreFactory.CreateJobStore()` pointing to Neon
   - Jobs survive process restarts
   - Scheduler detects overlaps (prevents concurrent job runs)
4. **Three Docker services**:
   ```yaml
   services:
     api:
       image: molaslubes-api:latest
       ports: ["5000:5000"]
       depends_on: [cache-db, neon-db]
       
     sync-worker:
       image: molaslubes-sync-worker:latest
       depends_on: [cache-db, neon-db]
       
     scraper-worker:
       image: molaslubes-scraper-worker:latest
       depends_on: [cache-db, neon-db]
   ```

### Jobs Delivered

**SyncWorker** (8 jobs):
- InvoiceSyncJob, PaymentSyncJob, DeliverySyncJob (CRITICAL)
- NeonInvoicePushJob, NeonPaymentPushJob, NeonDeliveryPushJob (CRITICAL)
- CustomerSyncJob, SalesOrderSyncJob (IMPORTANT)

**ScraperWorker** (4 jobs):
- LiquiMolyProductScrapeJob (OPTIONAL, daily 02:00 UTC)
- MeguinProductScrapeJob (OPTIONAL, daily 03:00 UTC)
- GermaxProductScrapeJob (OPTIONAL, daily 04:00 UTC)
- ProductDeltaSyncJob (IMPORTANT, hourly)

### Deployment Considerations

**Start Order**:
```
1. NeonDb (PostgreSQL) — must be first
2. MolasCacheDb (SQL Server) — can be parallel
3. MolasLubes.Api.exe — needs both DBs ready
4. MolasLubes.SyncWorker.exe — reads Cache, writes Neon
5. MolasLubes.ScraperWorker.exe — low priority
```

**Graceful Shutdown**:
```
1. Stop API (no new requests)
2. Stop SyncWorker (drains job queue, commits pending Neon writes)
3. Stop ScraperWorker (aborts enrichment, can resume on restart)
4. Shutdown cache DB, then Neon
```

**Monitoring & Observing**:
- Three separate log streams → aggregate in ELK/Splunk
- Each service reports startup/shutdown to central logger
- Heartbeat from each service every 60s (proves liveness)

### Success Criteria

| Metric | Target | How to Verify |
|--------|--------|---|
| **Api uptime independent of Worker restarts** | 100% | Restart SyncWorker, verify Api still responds to GET /health |
| **Job execution survives process crash** | 100% | Kill SyncWorker mid-job, restart, verify job resume logic |
| **Quartz prevents duplicate execution** | 0 races | Start 2 SyncWorker instances, verify only 1 runs InvoiceSyncJob |
| **Scraper doesn't block critical syncs** | <5% latency impact | Time: Neon write during scraper run vs without |
| **Graceful shutdown completes in <30s** | ≤30s | Measure: SIGTERM → last commit → process death |
| **Three-service scaling** | Independent restart capability | Kill any one service, restart within 5min, full recovery |

### Exit Criteria (To Advance to Tier D)
- [ ] Physical separation verified: no cross-process call blocking
- [ ] Quartz clustering working: dual-start test passes
- [ ] 30-day stability: zero unexpected process crashes
- [ ] Per-service SLAs defined (P99 latencies, % uptime)
- [ ] Monitoring dashboards show independent service health
- [ ] Graceful shutdown tested with real workloads
- [ ] Runbook for common failures documented
- [ ] Cloud deployment tested (if moving to K8s)

---

## Tier D: Advanced Operational Scaling

### Vision
Three-service architecture with independent operational policies, advanced monitoring, and multi-region readiness.

### Features (Planned)

1. **Throttling by CRITICAL/OPTIONAL Lane**
   - CRITICAL sync jobs: minimum 30s frequency (can't pause)
   - IMPORTANT jobs: batch hourly if Neon load detected
   - OPTIONAL jobs: pause entirely if `neon_connections_used / pool_size > 85%`
   - Implementation: `JobExecutor.CanRunJob(JobTier tier)` check before fire

2. **Distributed Caching (Redis)**
   - Cache layer: `127.0.0.1:6379` (Docker-compose service)
   - Api populates Redis on SAP writes
   - SyncWorker pre-fetches hot item codes from Redis
   - Expected gain: Neon reads drop 40% (most jobs cache-hit)

3. **Per-Lane SLAs & Alerting**
   - CRITICAL: alert if any job fails 2x in succession
   - IMPORTANT: alert if batch window exceeds 10min
   - OPTIONAL: silent retry, log only
   - Dashboard: SyncsPerHour, AvgSyncDuration, PushSuccessRate

4. **Multi-Region Setup (Future)**
   - Secondary Neon DB in EU region (read-only replica)
   - Scraper.exe can run in parallel in multiple regions
   - Odoo push geo-optimized (closest region → closest Odoo instance)

5. **Circuit Breaker on Neon Overload**
   - If Neon latency > 5s for 3 consecutive jobs: auto-pause OPTIONAL syncs
   - Alert: "Neon degraded, pausing enrichment"
   - Resume when latency < 1.5s again

### Sample Implementation Outline (Pseudo-code)

```csharp
// Tier D: Lane-Aware Job Throttling
public class TierDAwareJobExecutor
{
    public async Task ExecuteJobAsync(IJob job, IJobExecutionContext ctx)
    {
        var jobTier = Determined(job);  // CRITICAL, IMPORTANT, OPTIONAL
        
        if (jobTier == JobTier.OPTIONAL)
        {
            var neonHealth = await _healthCheck.GetNeonConnectionUsageAsync();
            if (neonHealth > 85)  // Connection pool exhausted
            {
                await ctx.Scheduler.PauseJob(ctx.JobDetail.Key);
                _telemetry.LogEvent("OptionalJobPaused", new { 
                    job = job.Name, 
                    poolUsage = neonHealth 
                });
                return;
            }
        }
        
        // Otherwise, execute normally
        await job.Execute(ctx);
    }
}

// Circuit breaker: auto-pause on Neon degradation
public class NeonCircuitBreaker
{
    public async Task MonitorAsync()
    {
        while (true)
        {
            var latency = await _neonDb.Database.ExecuteSqlRawAsync(
                "SELECT 1 FROM pg_prepare_statements LIMIT 1"
            );
            
            if (latency > 5000)  // 5 second threshold
                await _scheduler.PauseJobsInTier(JobTier.OPTIONAL);
            else if (latency < 1500)
                await _scheduler.ResumeJobsInTier(JobTier.OPTIONAL);
            
            await Task.Delay(60000);  // Check every 60s
        }
    }
}
```

### Tier D Timeline
- **Estimated start**: After 30 days of Tier C stability
- **Duration**: 2-3 sprints (enrichment + monitoring)
- **Launch gate**: Neon successfully handles 10x traffic spike during scraper window

---

## Decision Tree: When to Advance Tier

```
START (Tier A)
    │
    ├─ All sync jobs <5min to complete? ──→ YES ──→ Ready for Tier B
    └─ Any job failures blocking API?    ──→ YES ──→ MUST advance
    
TIER B (In-Process)
    │
    ├─ Quartz RAM job store sufficient? ──→ YES ──→ Run 7 days
    ├─ Neon latency <1s consistently?    ──→ YES ──→ Good
    ├─ Zero deadlocks in test week?      ──→ YES ──→ Ready for C
    └─ Can afford brief downtime?        ──→ NO  ──→ MUST migrate DB-backed Quartz first
    
TIER C (Physical Split)
    │
    ├─ Three services run 30 days?       ──→ YES ──→ Good
    ├─ Independent process restarts work?──→ YES ──→ Good
    ├─ Odoo push success rate ≥99%?      ──→ YES ──→ Good
    └─ Ready for advanced monitoring?    ──→ NO  ──→ Wait, don't jump to D
    
TIER D (Advanced Ops)
    │
    └─ All Tier C metrics stable + happy? ──→ YES ──→ Launch Tier D
```

---

## Rollback Plan

### If Tier B Fails
**Rollback**: Rebuild single MolasLubes.Api without SyncWorker
- Remove Quartz from DI
- Remove `MolasLubes.SyncWorker` reference
- Drop all Neon-related DbContexts
- Restore Tier A state
- **Recovery time**: ~2 hours (redeploy, re-test Cache-only reads)

### If Tier C Fails (Worst Case)
**Scenario**: SyncWorker crashing, jobs not running, Neon falling behind
**Rollback**:
1. Stop SyncWorker
2. Quartz jobs persisted in Neon — manually mark as "skip next"
3. Restart SyncWorker
4. If still failing: restore to Tier B (in-process) by rebuilding Api.exe with SyncWorker DLL
**Recovery time**: ~4 hours (understand root cause, patch, redeploy)

### If Tier D Fails
**Scenario**: Circuit breaker too aggressive, essential syncs paused
**Rollback**: Disable Tier D throttling, revert to Tier C
- Remove `TierDAwareJobExecutor`
- Restore simple job executor
- Verify all jobs run
**Recovery time**: ~30 minutes (rebuild, redeploy)

---

## Summary: Staged Risk Reduction

| Tier | Risk Level | Uptime Requirement | Duration |
|------|---|---|---|
| **A → B** | LOW | 4-hour windows ok | 2 weeks |
| **B → C** | MEDIUM | 1-hour window needed | 4 weeks (7d stability + migration) |
| **C → D** | MEDIUM | 30min window ok | 2 weeks (advanced features) |

**Key Insight**: Each tier adds operational complexity, but gains operational resilience. Don't skip tiers; advances only after stability proven.

---

**Document Version**: 1.0  
**Status**: Reference for implementation roadmap safety  
**Last Updated**: April 4, 2026
