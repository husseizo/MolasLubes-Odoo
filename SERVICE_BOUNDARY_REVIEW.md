# MolasLubes Service Boundary Review

**Purpose**: Clarify what belongs in each service when split from monolithic architecture into three separate Windows services.

**Context**: Tier C of the roadmap proposes splitting:
- `MolasLubes.Api` — HTTP endpoints only
- `MolasLubes.SyncWorker` — Scheduled jobs (SAP→Cache→Neon→Odoo)
- `MolasLubes.ScraperWorker` — Enrichment jobs (LiquiMoly/Meguin/Germax)

---

## Current State (Everything in One Service)

**Location**: `src/MolasLubes.Api/Program.cs`

All of the following are registered in a single DI container and run in one Windows Service:

```
MolasLubes.Api (monolithic)
├── HTTP Controllers (Invoices, Payments, Customers, Products, etc.)
├── SAP DI API Connection (COM interop)
├── Cache Services (ProductCacheService, InvoiceCacheService, etc.)
├── Domain Services (CustomerPricingService, OrderLifecycleService, etc.)
├── Neon Sync Services (NeonInvoiceSyncService, NeonPaymentSyncService, etc.)
├── LiquiMoly/Meguin/Germax Scrapers
├── Odoo Push Services
├── Quartz Jobs (all 15+)
└── Logging, Config, Health Checks
```

**Problem with monolithic approach**:
- API request thread pool blocked by slow Quartz sync jobs
- Long-running scraper job can't be paused without stopping API
- Hard to restart worker without stopping all HTTP endpoints
- Hard to fail gracefully (e.g., scraper network timeout affects sync jobs)

---

## Proposed State (Three Services)

### Service 1: MolasLubes.Api

**Purpose**: Handle HTTP requests synchronously; immediate SAP read/write operations

**Current files that stay**:
- `src/MolasLubes.Api/Controllers/*` — All controllers
- `src/MolasLubes.Api/Program.cs` — Modified (no Quartz)
- `src/MolasLubes.Api/appsettings.json` — Configuration

**Services registered**:

```
SAP Integration (Readers + Writers):
  • SapDiApiConnection (singleton, COM object)
  • SapProductReader, SapCustomerReader, SapSalesOrderReader, etc.
  • SapProductWriter, SapCustomerWriter, SapInvoiceWriter, etc.
  • SapCreditMemoReader, SapPaymentReader, etc.

Cache Services (read for immediate requests, write for backfill):
  • ProductCacheService
  • CustomerCacheService
  • DeliveryCacheService
  • SalesOrderCacheService
  • InvoiceCacheService
  • PaymentCacheService

Domain Services (business logic):
  • CustomerPricingService
  • CustomerCreditService
  • OrderLifecycleService
  • InvoiceBalanceService
  • SalesOrderStatusService
  • StockReservationService
  • ReservationCommitService
  • CancelSalesOrderService
  • InventoryCountingUomBackfillService
  • BulkInventoryCountingUomBackfillService
  • LiquiMolyTransferService
  
LiquiMoly Replenishment (human-triggered workflows):
  • LiquiMolyRoleService
  • LiquiMolyReplenishmentService (executes user-approved replenishment)
  • LiquiMolyReplenishmentExecutionService
  • SapLiquiMolyDemandReader
  • SapLiquiMolySourceMapReader

Http Clients:
  • OdooApiClient (for direct Odoo queries, not push jobs)

Idempotency:
  • IdempotencyService (prevents duplicate SAP transactions from retried requests)

Supporting:
  • Logging (Serilog)
  • Configuration
  • Health checks (/health/live, /health/ready)
  • API key validation
```

**Listens on**: `http://localhost:5000`

**Does NOT listen to**:
- Quartz scheduling (removed)

**Key interaction pattern**:
- HTTP request → SAP read → Cache write → Response
- Example: `POST /api/invoices` → Read SAP via DI API → Write to cache → Schedule sync job message (async)

**Restart requirement**: Any time config/schema changes

**Typical latency**: 50-500ms per request

---

### Service 2: MolasLubes.SyncWorker

**Purpose**: Run scheduled jobs that sync data across the 3-layer pipeline

**Current files that move/change**:
- Create new: `src/MolasLubes.SyncWorker/Program.cs` (new entry point)
- Move/reference: All sync-related services from Api

**Quartz Jobs registered**:

#### Layer 1: SAP → Cache (reads cache, syncs deltas from SAP)
```
RegisterJob<ProductFullSyncJob>("ProductFullSyncJob", "0 0 */6 ? * *");        // Every 6 hours (OPTIONAL)
RegisterJob<CustomerDeltaSyncJob>("CustomerDeltaSyncJob", "0 */5 * ? * *");    // Every 5 min (CRITICAL)
RegisterJob<SalesOrderSyncJob>("SalesOrderSyncJob", "10 */5 * ? * *");         // Every 5 min (CRITICAL)
RegisterJob<SapOpenOrdersSyncJob>("SapOpenOrdersSyncJob", "20 */5 * ? * *");   // Every 5 min (CRITICAL)
RegisterJob<DeliveryDeltaSyncJob>("DeliveryDeltaSyncJob", "0/10 * * ? * *");   // Every 10 sec (CRITICAL)
RegisterJob<InvoiceSyncJob>("InvoiceSyncJob", "3/10 * * ? * *");               // Every 10 sec (CRITICAL)
RegisterJob<PaymentSyncJob>("PaymentSyncJob", "6/10 * * ? * *");               // Every 10 sec (CRITICAL)
RegisterJob<QuotationToSalesOrderJob>("QuotationToSalesOrderJob", "0 */2 * ? * *");  // Every 2 min (CRITICAL)
```

**Criticality Tiers** (for scheduling and throttling strategy):

| Tier | Components | SLA | Failure Action |
|------|-----------|-----|---|
| **CRITICAL** | Invoices, Payments, Deliveries, open orders, quotes, customer metadata | Must sync every 30s; failures alert ops within 5 min | Retry immediately; escalate if 3+ consecutive failures |
| **OPTIONAL** | Product data (full refresh), pricing (catch-up), non-core refreshes | Can batch hourly; optional if behind | Retry on next cycle; alert if 12+ hours behind |

**Why This Distinction Matters**:
- Later: Can pause OPTIONAL syncs without affecting transaction flow
- Later: Can set different Quartz thread pools (1 for CRITICAL, 1 for OPTIONAL)
- Failure handling: Critical path failures → immediate alert; optional → best-effort
- Throttling: Can rate-limit OPTIONAL jobs if Neon gets overloaded

#### Layer 2: Cache → Neon (reads cache, syncs to Neon)
```
RegisterJob<NeonCustomerSyncJob>("NeonCustomerSyncJob", "5 */5 * ? * *");      // Every 5 min (CRITICAL)
RegisterJob<NeonProductDeltaSyncJob>("NeonProductDeltaSyncJob", "55 */10 * ? * *");  // Every 10 min (OPTIONAL)
RegisterJob<NeonInvoiceSyncJob>("NeonInvoiceSyncJob", "4/10 * * ? * *");       // Every 10 sec (CRITICAL)
RegisterJob<NeonPaymentSyncJob>("NeonPaymentSyncJob", "7/10 * * ? * *");       // Every 10 sec (CRITICAL)
RegisterJob<NeonDeliverySyncJob>("NeonDeliverySyncJob", "1/10 * * ? * *");     // Every 10 sec (CRITICAL)
RegisterJob<NeonSalesOrderSyncJob>("NeonSalesOrderSyncJob", "15 */5 * ? * *"); // Every 5 min (CRITICAL)
RegisterJob<NeonSalesOrderLineSyncJob>("NeonSalesOrderLineSyncJob", "25 */5 * ? * *");  // Every 5 min (CRITICAL)
RegisterJob<NeonPriceListSyncJob>("NeonPriceListSyncJob", "[schedule TBD]");   // TBD (OPTIONAL)
```

#### Layer 3: Neon → Odoo (reads Neon, pushes to Odoo)
```
RegisterJob<OdooDeliveryPushJob>("OdooDeliveryPushJob", "2/10 * * ? * *");     // Every 10 sec (CRITICAL)
RegisterJob<OdooInvoicePushJob>("OdooInvoicePushJob", "5/10 * * ? * *");       // Every 10 sec (CRITICAL)
RegisterJob<OdooPaymentPushJob>("OdooPaymentPushJob", "8/10 * * ? * *");       // Every 10 sec (CRITICAL)
```

**Services registered**:

```
SAP Integration (readers only, NO writers):
  • SapProductReader
  • SapCustomerReader
  • SapSalesOrderReader
  • SapDeliveryReader
  • SapInvoiceReader
  • SapPaymentReader
  • SapPricingReader
  • SapCreditMemoReader

Cache Services (read only):
  • ProductCacheService (reads cache to check against SAP)
  • CustomerCacheService
  • DeliveryCacheService
  • SalesOrderCacheService
  • InvoiceCacheService
  • PaymentCacheService

Neon Sync Services (the core 8):
  • NeonCustomerSyncService
  • NeonDeliverySyncService
  • NeonInvoiceSyncService
  • NeonPaymentSyncService
  • NeonSalesOrderSyncService
  • NeonSalesOrderLineSyncService
  • ProductNeonSyncService
  • PriceListNeonSyncService

Odoo Push Services:
  • OdooDeliveryPushService
  • OdooInvoicePushService
  • OdooPaymentPushService
  • OdooApiClient (for batch push queries)

Background Service:
  • NeonKeepAliveService (prevents Neon connection idle timeout)

Supporting:
  • Logging (Serilog)
  • Configuration
  • Health checks (/health/live, /health/ready)
```

**Listens on**: Nothing (no HTTP; internal only)

**Does NOT register**:
- HTTP controllers
- SAP writers (create, cancel, update operations)
- Scrapers (moved to ScraperWorker)
- API key validation (not needed)

**Key interaction pattern**:
- Quartz timer fires → Load data from Cache/SAP → Sync to Neon → Push to Odoo
- Example: Every 30s: `(Cache.Invoices where CachedAt > lastSync) → Neon`

**Restart requirement**: When sync logic changes, or when job schedule needs adjustment

**Typical latency**: Each job takes 2-5 seconds (reads + writes + retries)

---

### Service 3: MolasLubes.ScraperWorker

**Purpose**: Run enrichment jobs that scrape external sites (LiquiMoly, Meguin, Germax) and update product data

**Current files that move/change**:
- Create new: `src/MolasLubes.ScraperWorker/Program.cs` (new entry point)
- Move: Scraper-related jobs and services

**Quartz Jobs registered**:

```
RegisterJob<LiquiMolyProductScrapeJob>("LiquiMolyProductScrapeJob", "0 0 2 ? * *");    // Nightly @ 02:00 UTC
RegisterJob<MeguinScraperJob>("MeguinScraperJob", "[schedule TBD]");                   // TBD frequency
RegisterJob<GermaxProductEnrichmentJob>("GermaxProductEnrichmentJob", "0 30 1 ? * *"); // Nightly @ 01:30 UTC
RegisterJob<GermaxRetryFailedJob>("GermaxRetryFailedJob", "0 0 9,21 ? * *");           // 09:00 and 21:00 UTC
```

**Services registered**:

```
HTTP Clients (with long timeouts for slow scrapers):
  • LiquiMolyProductScraperService (120s timeout)
  • MeguinProductScraperService (120s timeout)
  • GermaxProductScraperService (30s timeout)

Sync Services (upsert scraped data):
  • LiquiMolyCacheSyncService (write to cache)
  • LiquiMolyNeonSyncService (write to Neon)
  • GermaxCacheSyncService
  • GermaxAutoHubSyncService

Data Access (for AutoHub profile):
  • SapAutoHubSeedReader

Supporting:
  • Logging (Serilog)
  • Configuration
  • Health checks (/health/live, /health/ready)
```

**Listens on**: Nothing (no HTTP; internal only)

**Does NOT register**:
- HTTP controllers
- Transaction sync services (Neon*)
- Odoo push services
- SAP DI readers/writers

**Key interaction pattern**:
- Quartz timer fires @ 02:00 UTC → Scrape LiquiMoly site → Extract product data → Upsert to Cache + Neon

**Restart requirement**: When scraper config/URLs change, or when enrichment logic needs update

**Typical latency**: Each job takes 5-60 minutes (many HTTP requests, polite crawling delays)

---

## Shared Components (All Three Services Reference)

### Database Contexts (Defined in Infrastructure)

All three services register these DbContexts from same project:

```csharp
builder.Services.AddDbContext<MolasCacheDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("MolasCacheDb")));

builder.Services.AddDbContext<NeonDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("NeonDb"),
        npgsql => {
            npgsql.MigrationsAssembly("MolasLubes.Infrastructure");
            npgsql.CommandTimeout(120);
            npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
        }));

builder.Services.AddDbContext<AutoHubDbContext>(options =>
    options.UseNpgsql(...));
```

### Configuration (Shared)

- `appsettings.json` (same for all three)
- Connection strings (same for all three)
- Logging settings (Serilog, same for all three)
- Secrets (dotnet user-secrets, same for all three on dev machine)

### Infrastructure Layer

Everything in `src/MolasLubes.Infrastructure/`:
- `Persistence/` — DbContexts (shared)
- `Integrations/` — External clients (API's now *read-only*, Worker's read, Scraper's for scraping)
- `Services/` — All application services (shared)
- `Scheduling/` — Quartz job implementations (split between Worker and ScraperWorker)

### Domain Entities

- `src/MolasLubes.Domain/` — All entities (shared, unchanged)

### Application DTOs

- `src/MolasLubes.Application/` — All DTOs (shared, unchanged)

---

## Dependency Flow (Post-Split)

```
┌──────────────────────────────────────────────────────────────────┐
│                    SHARED INFRASTRUCTURE                         │
│  └─ DbContexts, Entities, Services, Logging, Configuration       │
└──────────────────────────────────────────────────────────────────┘

┌─────────────────────┐    ┌──────────────────┐    ┌────────────────────┐
│   MolasLubes.Api    │    │ MolasLubes.      │    │ MolasLubes.Scraper │
│   (HTTP Requests)   │    │ SyncWorker       │    │ Worker             │
│                     │    │ (Scheduled Sync) │    │ (Enrichment)       │
│ Controllers ──────┐ │    │                  │    │                    │
│ SAP Readers/      │ │    │ Sync Jobs ─────┐ │    │ Scraper Jobs ──┐  │
│ Writers           │ │    │ Cache Readers  │ │    │ URL crawling   │  │
│ Cache Services    │ │───→ Neon Services  │ │    │ Data extraction│  │
│ Domain Services   │ │    │ Odoo Services  │ │    │ Upsert Cache   │  │
│                   │ │    │                └─┼────→ Upsert Neon    │  │
└───────────────────┘ │    └──────────────────┘    │                │  │
                      │                            └────────────────┘  │
                      │                                                 │
                      └──────────────────────────────────────────────┐ │
                                                   (async notification) │
                                                   (or pull on schedule)
```

---

## Key Decisions & Rationale

### 1. Why SAP Stays in API Service (For Now, But With Caveats)

**Current Boundary (Tier C+)**:
- SAP **write path** (create, cancel, update) → API service only
- SAP **read path for synchronous requests** (current inventory, pricing) → API service only
- SAP **scheduled reads for syncs** (ProductFullSyncJob, CustomerDeltaSyncJob) → Currently in API, may move

**Reasoning (Why This Works Now)**:
- SAP COM interop is stateful and Windows-bound
- Most SAP writes are synchronous HTTP requests (create invoice, record payment) with immediate response needed
- Latency is already 100-500ms; no benefit to distribute SAP writes
- Simplifies SAP state management (no remote IPC layer, single COM session)

**Why Not Move SAP Reads to SyncWorker Yet**:
- COM session stability in background worker is unproven
- SAP scheduled syncs currently run infrequently enough (5-10 min intervals, not critical path)
- Keeps operational complexity lower in Tier C

**Potential Future Decision (Tier D+, if needed)**:
- If SAP becomes bottleneck, move **scheduled read jobs** (ProductFullSync, CustomerDeltaSync) to SyncWorker
- Prerequisite: Prove COM session is stable across long background process lifetime
- SAP writes stay in API (synchronous request/response pattern)
- This would require:
  - Testing COM session stability under 24/7 operation
  - Adding SAP reader ADO dependency to SyncWorker
  - Maintaining two SAP integration points (writer in Api, reader in Worker)

**Mark This As**: ✅ Current recommended boundary | ❌ Not necessarily permanent

### 2. Why Odoo Pushes Go to SyncWorker (Not ScraperWorker)

**Reasoning**:
- Odoo push jobs read from Neon (transactional data), not external enrichment
- They sync frequently (currently every 10s → 30s in Tier A)
- Logical grouping: "sync jobs" not "scraper jobs"
- Failure correlation: If Odoo is down, other syncs still work

### 3. Why ScraperWorker is Separate

**Reasoning**:
- Scrapers are fault-tolerant-able: Site down → retry later, doesn't block transactions
- Different SLA: "best effort nightly" vs "must succeed every 30s"
- Network isolation: Scraper timeouts don't affect sync worker thread pool
- Easy to disable: Can stop scraper without affecting sales order flow

### 4. Why Process Restart Boundaries Are Clear

| Service | Restart Without Affecting | Why |
|---------|--------------------------|-----|
| **Api** | SyncWorker, ScraperWorker | Independent processes |
| **SyncWorker** | Api, ScraperWorker | Independent processes |
| **ScraperWorker** | Api, SyncWorker | Independent processes |

**Real scenario**: LiquiMoly site is down; scraper job timeouts; SyncWorker unaffected because it's a separate process.

---

## Service Registration Summary

### MolasLubes.Api (Tier A + B focused)

**Keep**:
- 25+ SAP reader/writer services
- 6 cache services
- 13 domain services
- LiquiMoly replenishment services (user-triggered)
- Controllers
- HTTP clients (Odoo for queries, not batch push)

**Remove**:
- All Quartz jobs
- All Neon sync services (they're for async syncs, not request/response)
- LiquiMolyProductScrapeJob
- Scrapers

---

### MolasLubes.SyncWorker (Tier A + B focused)

**Add**:
- All Neon sync services (8 of them)
- All Quartz jobs for layers 1-3 sync (16 total)
- OdooApiClient for batch queries
- NeonKeepAliveService

**Remove**:
- SAP writers (Api handles creates/updates)
- Scrapers
- Controllers

---

### MolasLubes.ScraperWorker (Lower priority; can start post-Tier-B)

**Add**:
- 4 scraper Quartz jobs
- 3 scraper HTTP client services
- 4 sync services (LiquiMoly, Germax, Meguin)

**Remove**:
- Everything else

---

## Configuration Per Service

Each service gets its own `appsettings.{env}.json` but they're mostly identical:

```json
{
  "ConnectionStrings": {
    "MolasCacheDb": "Server=...;Database=MolasCacheDb;...",
    "NeonDb": "Host=...;Database=MolasLUBES;Username=...;Password=...;..."
  },
  "Logging": { ... },
  "Quartz": {
    "quartz.scheduler.instanceName": "MolasLubesApi" | "MolasLubesSyncWorker" | "MolasLubesScraperWorker",
    "quartz.jobStore.type": "RAMJobStore"  // Simple in-memory; can scale to persistent store later
  },
  "LiquiMolyScraper": { ... },
  "MeguinScraper": { ... },
  "GermaxScraper": { ... },
  "OdooApi": { ... },
  "SyncSettings": { "EnableNeonInvoiceSync": true, ... }
}
```

---

## Actual Implementation Steps (When Ready)

If/when Tier C is decided:

### Phase 1: Create SyncWorker Project Structure
```
src/MolasLubes.SyncWorker/
├── Program.cs (entry point, registers Quartz jobs only)
├── appsettings.json (config, mostly copy from Api)
├── MolasLubes.SyncWorker.csproj
└── (services are referenced from Infrastructure project)
```

### Phase 2: Create ScraperWorker Project Structure
```
src/MolasLubes.ScraperWorker/
├── Program.cs (entry point, registers scraper jobs only)
├── appsettings.json
├── MolasLubes.ScraperWorker.csproj
└── (services are referenced from Infrastructure project)
```

### Phase 3: Modify Api Program.cs
- Remove Quartz registration
- Keep all other services
- Deploy as `MolasLubes.Api` service

### Phase 4: Deploy as Three Windows Services
```
Service 1: MolasLubes.Api (port 5000)
Service 2: MolasLubes.SyncWorker (background)
Service 3: MolasLubes.ScraperWorker (background)
```

---

## Open Questions (For Discussion)

1. **Should SyncWorker have an HTTP health endpoint?**
   - Current: Only health checks via log output
   - Proposal: Add `/health/live` and `/health/ready` for monitoring
   - Monitoring could ping: `http://localhost:5001/health/ready`

2. **Should jobs run sequentially or parallel within each worker?**
   - Current: Quartz MaxConcurrency = 1 (serialized)
   - Proposal post-split: SyncWorker could run multiple jobs in parallel (separate Quartz thread pools per job type)
   - ScraperWorker: Sequential (only 4 jobs, mostly serial anyway)

3. **Should API log sync job outcomes?**
   - Current: Api doesn't know about sync job results
   - Proposal: SyncWorker writes result to log; Api queries logs for audit
   - Trade-off: Loose coupling vs observability

4. **Should there be a shared message queue between Api and SyncWorker?**
   - Current: None; sync jobs run on schedule
   - Proposal (later, Tier E): Api could queue "manual sync" requests for SyncWorker
   - Trade-off: Adds complexity; current schedule-based approach is fine for now

---

## Service Boundary Violations: Architectural Guardrails

### ❌ VIOLATION #1: Api Writes to Neon in Background

**Red Flag Code**:
```csharp
// MolasLubes.Api/Controllers/InvoiceController.cs ❌ WRONG
[HttpPost("invoices")]
public async Task<IActionResult> CreateInvoice(CreateInvoiceRequest req)
{
    var invoice = _sapInvoiceWriter.CreateInvoice(...);
    _cacheDb.CacheInvoices.Add(invoice);
    await _cacheDb.SaveChangesAsync();
    
    // ❌ VIOLATION: Background Neon write from request handler
    _ = Task.Run(async () =>
    {
        var neonInvoice = Map(invoice);
        _neonDb.Invoices.Add(neonInvoice);
        await _neonDb.SaveChangesAsync();  // Fire-and-forget
    });
    
    return Ok(invoice);
}
```

**Correct Pattern**:
```csharp
// MolasLubes.Api/Controllers/InvoiceController.cs ✅ CORRECT
[HttpPost("invoices")]
public async Task<IActionResult> CreateInvoice(CreateInvoiceRequest req)
{
    var invoice = _sapInvoiceWriter.CreateInvoice(...);
    
    // ✅ ONLY: Write to Cache
    _cacheDb.CacheInvoices.Add(invoice);
    await _cacheDb.SaveChangesAsync();
    
    // → SyncWorker will pick it up from Cache on next run
    return Ok(invoice);
}
```

**Guard**: Api must NOT have `NeonDb` in DI container (except test fixtures)

---

### ❌ VIOLATION #2: SyncWorker Creates/Modifies Invoice Data

**Red Flag Code**:
```csharp
// MolasLubes.SyncWorker/Jobs/NeonInvoiceSyncJob.cs ❌ WRONG
public async Task Execute(IJobExecutionContext context)
{
    // ❌ VIOLATION: Creating new invoice (source should be SAP)
    var newInvoice = new InvoiceEntity
    {
        Number = "INV-999999",
        Amount = 1500,
        CustomerId = 123
    };
    _neonDb.Invoices.Add(newInvoice);
    await _neonDb.SaveChangesAsync();
}
```

**Correct Pattern**:
```csharp
// MolasLubes.SyncWorker/Jobs/NeonInvoiceSyncJob.cs ✅ CORRECT
public async Task Execute(IJobExecutionContext context)
{
    // ✅ Read from Cache (which came from SAP)
    var changedInvoices = await _cacheDb.CacheInvoices
        .Where(x => x.CachedAt > _lastSyncTime)
        .ToListAsync();
    
    // ✅ Transform and upsert to Neon
    foreach (var inv in changedInvoices)
    {
        var neonInv = Map(inv);
        var existing = await _neonDb.Invoices
            .FirstOrDefaultAsync(x => x.SourceKey == neonInv.SourceKey);
        
        if (existing != null)
            existing.UpdateFrom(neonInv);
        else
            _neonDb.Invoices.Add(neonInv);
    }
    
    await _neonDb.SaveChangesAsync();
}
```

**Guard**: SyncWorker must NOT have SAP writers in DI

---

### ❌ VIOLATION #3: SyncWorker Routes User Requests

**Red Flag Code**:
```csharp
// MolasLubes.SyncWorker/Program.cs ❌ WRONG
var builder = WebApplicationBuilder.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.MapControllers();  // ← This makes SyncWorker a web server
app.Run();
```

**Correct Pattern**:
```csharp
// MolasLubes.SyncWorker/Program.cs ✅ CORRECT
var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices(services =>
{
    services.AddQuartz(q =>
    {
        q.AddJob<InvoiceSyncJob>(opts => 
            opts.WithIdentity(nameof(InvoiceSyncJob)));
    });
    
    services.AddDbContext<MolasCacheDb>();
    services.AddDbContext<NeonDb>();
    services.AddScoped<NeonInvoiceSyncService>();
});

var host = builder.Build();
await host.RunAsync();  // ← Console app, not web server
```

**Guard**: SyncWorker.csproj must NOT reference `Microsoft.AspNetCore.App`

---

### ❌ VIOLATION #4: Api Calls Quartz Scheduler

**Red Flag Code**:
```csharp
// MolasLubes.Api/Controllers/AdminController.cs ❌ WRONG
[HttpPost("admin/trigger-sync")]
public async Task<IActionResult> TriggerSync()
{
    // ❌ VIOLATION: Api controls SyncWorker jobs
    await _scheduler.TriggerJob(new JobKey(nameof(InvoiceSyncJob)));
    return Ok("Sync triggered");
}
```

**Correct Pattern**:
```csharp
// MolasLubes.Api/Controllers/AdminController.cs ✅ CORRECT
[HttpPost("admin/trigger-sync")]
public IActionResult TriggerSync()
{
    return BadRequest("Manual sync not supported; jobs run on schedule");
    
    // OR (if needed): Api could publish a message queue event
    // await _messageQueue.PublishAsync(new ManualSyncRequest(...));
}
```

**Guard**: Api must NOT have `IScheduler` injected in any controller

---

### ❌ VIOLATION #5: ScraperWorker Modifies Transactional Data

**Red Flag Code**:
```csharp
// MolasLubes.ScraperWorker/Jobs/ProductEnrichmentJob.cs ❌ WRONG
public async Task Execute(IJobExecutionContext context)
{
    // OK: Enrich products
    var products = await _liquiMolyScraper.ScrapeAsync();
    foreach (var p in products)
        _cacheDb.LiquiMolyProducts.Add(p);
    await _cacheDb.SaveChangesAsync();
    
    // ❌ VIOLATION: Modifying transactional data (invoice)
    var oldInvoices = await _neonDb.Invoices
        .Where(x => x.CreatedAt < DateTime.UtcNow.AddDays(-90))
        .ToListAsync();
    _neonDb.Invoices.RemoveRange(oldInvoices);
    await _neonDb.SaveChangesAsync();  // Should be SyncWorker only
}
```

**Correct Pattern**:
```csharp
// MolasLubes.ScraperWorker/Jobs/ProductEnrichmentJob.cs ✅ CORRECT
public async Task Execute(IJobExecutionContext context)
{
    // ✅ ONLY: Enrich with external data
    var products = await _liquiMolyScraper.ScrapeAsync();
    foreach (var p in products)
    {
        var cached = await _cacheDb.LiquiMolyProducts
            .FirstOrDefaultAsync(x => x.ItemCode == p.ItemCode);
        
        if (cached != null)
            cached.ProductDescription = p.Description;  // Update enrichment
        else
            _cacheDb.LiquiMolyProducts.Add(p);
    }
    
    await _cacheDb.SaveChangesAsync();
    
    // ✅ DO NOT: Manipulate transactional data
    // If cleanup needed, create NeonCleanupJob in SyncWorker only
}
```

**Guard**: ScraperWorker must NOT have access to Neon transactional services

---

### ❌ VIOLATION #6: Circular Cross-Service Dependency

**Red Flag Code**:
```csharp
// MolasLubes.SyncWorker/Services/NeonInvoiceSyncService.cs ❌ WRONG
public class NeonInvoiceSyncService
{
    public NeonInvoiceSyncService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _apiBaseUrl = "http://localhost:5000/api";  // Calling Api back
    }
    
    public async Task PushToOdooAsync(Invoice inv)
    {
        var response = await _httpClient.PostAsync(
            $"{_apiBaseUrl}/invoices/validate",
            ...
        );
    }
}
```

**Correct Pattern**:
```csharp
// MolasLubes.SyncWorker/Services/NeonInvoiceSyncService.cs ✅ CORRECT
public class NeonInvoiceSyncService
{
    public async Task PushToOdooAsync(Invoice inv)
    {
        // ✅ ONLY: Call external Odoo API
        var response = await _odooClient.CreateInvoiceAsync(inv);
        
        // Never call Api back
    }
}
```

**Guard**: No service should have `localhost:5000` or `api.internal` in its HTTP clients

---

### ❌ VIOLATION #7: Scraper Does Odoo Pushes

**Red Flag Code**:
```csharp
// MolasLubes.ScraperWorker/Jobs/LiquiMolyProductScrapeJob.cs ❌ WRONG
public async Task Execute(IJobExecutionContext context)
{
    var products = await _liquiMolyScraper.ScrapeAsync();
    foreach (var p in products)
        _cacheDb.LiquiMolyProducts.Add(p);
    await _cacheDb.SaveChangesAsync();
    
    // ❌ VIOLATION: Scraper pushing to Odoo
    foreach (var p in products)
        await _odooClient.CreateProductAsync(p);  // Wrong service!
}
```

**Correct Pattern**:
```csharp
// MolasLubes.ScraperWorker/Jobs/LiquiMolyProductScrapeJob.cs ✅ CORRECT
public async Task Execute(IJobExecutionContext context)
{
    // ✅ ONLY: Scrape and cache
    var products = await _liquiMolyScraper.ScrapeAsync();
    foreach (var p in products)
    {
        var cached = await _cacheDb.LiquiMolyProducts
            .FirstOrDefaultAsync(x => x.ItemCode == p.ItemCode);
        
        if (cached != null)
            cached.UpdateFrom(p);  // Enrich only
        else
            _cacheDb.LiquiMolyProducts.Add(p);
    }
    
    await _cacheDb.SaveChangesAsync();
    
    // ✅ DO NOT: Push to Odoo
    // If product updates should sync: create NeonProductPushJob in SyncWorker
}
```

**Guard**: ScraperWorker must NOT have `IOdooApiClient` injected

---

## DI Container Verification Tests

### For MolasLubes.Api

```csharp
[Fact]
public void ApiStartup_HasCorrectDependencies()
{
    var startup = new ApiStartup();
    var services = new ServiceCollection();
    startup.ConfigureServices(services);
    var provider = services.BuildServiceProvider();
    
    // ✅ MUST have
    Assert.NotNull(provider.GetRequiredService<ISapInvoiceWriter>());
    Assert.NotNull(provider.GetRequiredService<MolasCacheDb>());
    
    // ❌ MUST NOT have
    Assert.Throws<InvalidOperationException>(() => 
        provider.GetRequiredService<NeonDb>());
    Assert.Throws<InvalidOperationException>(() => 
        provider.GetRequiredService<IScheduler>());
    Assert.Throws<InvalidOperationException>(() => 
        provider.GetRequiredService<NeonInvoiceSyncService>());
}
```

### For MolasLubes.SyncWorker

```csharp
[Fact]
public void SyncWorkerStartup_HasCorrectDependencies()
{
    var startup = new SyncWorkerStartup();
    var services = new ServiceCollection();
    startup.ConfigureServices(services);
    var provider = services.BuildServiceProvider();
    
    // ✅ MUST have
    Assert.NotNull(provider.GetRequiredService<IScheduler>());
    Assert.NotNull(provider.GetRequiredService<NeonDb>());
    Assert.NotNull(provider.GetRequiredService<NeonInvoiceSyncService>());
    
    // ❌ MUST NOT have
    Assert.Throws<InvalidOperationException>(() => 
        provider.GetRequiredService<ISapInvoiceWriter>());
    Assert.Throws<InvalidOperationException>(() => 
        provider.GetRequiredService<IControllerFactory>());
}
```

### For MolasLubes.ScraperWorker

```csharp
[Fact]
public void ScraperWorkerStartup_HasCorrectDependencies()
{
    var startup = new ScraperWorkerStartup();
    var services = new ServiceCollection();
    startup.ConfigureServices(services);
    var provider = services.BuildServiceProvider();
    
    // ✅ MUST have
    Assert.NotNull(provider.GetRequiredService<LiquiMolyProductScraper>());
    Assert.NotNull(provider.GetRequiredService<MolasCacheDb>());
    
    // ❌ MUST NOT have
    Assert.Throws<InvalidOperationException>(() => 
        provider.GetRequiredService<IOdooApiClient>());
    Assert.Throws<InvalidOperationException>(() => 
        provider.GetRequiredService<NeonInvoiceSyncService>());
}
```

---

## Code Review Checklist

Before approving any PR involving Tier C work, verify:

- [ ] **No Api→Neon background writes** (grep: `Task.Run` + `_neonDb`)
- [ ] **No SyncWorker→SAP writes** (grep: `ISapXxxWriter` in Worker)
- [ ] **No SyncWorker HTTP endpoints** (grep: `[HttpPost]`)
- [ ] **No Api→IScheduler calls** (grep: `_scheduler.TriggerJob`)
- [ ] **No Scraper→Odoo pushes** (grep: `_odooClient` in Scraper jobs)
- [ ] **No cross-service HTTP calls** (grep: `localhost:5000`)
- [ ] **DI verification tests pass** (run xUnit startup tests)
- [ ] **No new project references** between services

---

## Summary: Service Boundaries Are Clear Now

**Api**: Synchronous HTTP requests + immediate SAP operations  
**SyncWorker**: Scheduled data pipeline (SAP→Cache→Neon→Odoo)  
**ScraperWorker**: Fault-tolerant enrichment (LiquiMoly/Meguin/Germax)  

**Each service**:
- Has a single reason to change
- Has no circular dependencies
- Can be restarted independently
- Fails with clear boundaries
- Is independently debuggable

---

**Document Version**: 2.0  
**Status**: Reference for Tier C implementation + code review guardrails  
**Last Updated**: April 4, 2026
