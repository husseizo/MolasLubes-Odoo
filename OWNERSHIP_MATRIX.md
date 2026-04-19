# MolasLubes Ownership & Dependency Matrix

**Purpose**: Explicit boundaries for implementation safety. Shows what each service owns, what it may call, what it must not access, and where it can write data.

---

## Service Ownership Grid

### MolasLubes.Api (HTTP Request Handler)

| Aspect | Details | Notes |
|--------|---------|-------|
| **Owns** | HTTP Controllers, immediate SAP write operations, synchronous business logic | Only this service processes user requests |
| **May Call** | SAP DI API (reads + writes), Cache DB (reads + writes via services), Neon DB (reads for immediate context only, never background writes) | Patterns: sync request → read SAP → write Cache → respond |
| **May NOT** | Create/schedule Quartz jobs, write to Neon background data, call SyncWorker/ScraperWorker | Boundary is clean: Api ≠ Scheduler |
| **Can Write To** | MolasCacheDb (all tables), NeonDb (only reads or immediate context updates) | Write pattern: GET request → update cache → return |
| **Can Read From** | MolasCacheDb (all), NeonDb (all), SAP via DI | Read pattern: build context for immediate response |
| **Thread Safety** | Scoped DI per HTTP request; SAP connection is singleton (COM manages) | Each request gets fresh DbContext instances |
| **State Lifetime** | Request-scoped (100-500ms typical) | Stateless from service perspective |
| **Never Touches** | Quartz scheduler state, background job tables, scraper state | If these need touching, that's a code smell |

**Example Valid Pattern**:
```csharp
[HttpPost("invoices")]
public async Task<IActionResult> CreateInvoice(CreateInvoiceRequest req)
{
    // ✅ VALID: Read from SAP
    var customer = _sapCustomerReader.GetCustomer(req.CustomerId);
    
    // ✅ VALID: Write to SAP
    var invoiceEntry = _sapInvoiceWriter.CreateInvoice(...);
    
    // ✅ VALID: Write to cache
    _cacheDb.CacheInvoices.Add(...);
    await _cacheDb.SaveChangesAsync();
    
    // ❌ INVALID PATTERN (would indicate design issue):
    // await _neonDb.Invoices.Add(...); await _neonDb.SaveChangesAsync();
    // → Neon writes belong in SyncWorker only
    
    // ❌ INVALID: Don't schedule Quartz jobs from request handler
    // await _scheduler.ScheduleJob(...);
    
    return Ok(new { invoiceEntry });
}
```

---

### MolasLubes.SyncWorker (Scheduled Data Pipeline)

| Aspect | Details | Notes |
|--------|---------|-------|
| **Owns** | Quartz job scheduling & execution, 3-layer sync orchestration (SAP→Cache→Neon→Odoo) | Only this service runs background syncs |
| **May Call** | Cache DB (reads only), Neon DB (reads + writes), SAP DI readers (not writers), OdooApiClient (push operations only) | Patterns: Read Cache, transform, write Neon; Read Neon, push to Odoo |
| **May NOT** | Create HTTP endpoints, accept user requests, make arbitrary SAP writes, call scrapers | Boundary is clean: Worker ≠ Api |
| **Can Write To** | NeonDb (all transactional tables), MolasCacheDb (read-only for now; Tier D may change) | Upsert pattern: SELECT by key → UPDATE or INSERT |
| **Can Read From** | MolasCacheDb (all), NeonDb (all), SAP readers (currently; later may move to here) | Should be: "read input, compute, write Neon" |
| **Thread Safety** | Quartz manages job concurrency; each job execution gets scoped DbContext | MaxConcurrency controls parallelism |
| **State Lifetime** | Job-scoped (2-10 seconds typical) | Each job runs independently; no shared state between jobs |
| **Never Touches** | HTTP controllers, user request handlers, SyncFailureLog writes (separate service), SAP writers | If these need touching, it's a design issue |
| **Critical vs Optional Split** | See SERVICE_BOUNDARY_REVIEW.md for job tiers; use this to differ throttling/failure handling | Later: can pause OPTIONAL syncs independently |

**Example Valid Pattern**:
```csharp
// NeonInvoiceSyncJob.cs
public async Task Execute(IJobExecutionContext context)
{
    var strategy = _neonDb.Database.CreateExecutionStrategy();
    
    await strategy.ExecuteAsync(async () =>
    {
        // ✅ VALID: Read from cache (input)
        var changedInvoices = await _cacheDb.CacheInvoices
            .Where(x => x.CachedAt > lastSync)
            .ToListAsync();
        
        // ✅ VALID: Transform data
        var neonInvoices = changedInvoices.Select(x => Map(x)).ToList();
        
        // ✅ VALID: Write to Neon
        foreach (var inv in neonInvoices)
            _neonDb.Invoices.Add(inv);
        await _neonDb.SaveChangesAsync();
        
        // ❌ INVALID: Don't create new SAP invoices from sync job
        // await _sapInvoiceWriter.CreateInvoice(...);
        
        // ❌ INVALID: Don't write back to cache as "source of truth"
        // _cacheDb.CacheInvoices.Update(...); // Source is Cache, not Worker
        
        // ❌ INVALID: Don't call API endpoint
        // await _httpClient.PostAsync("http://api/endpoint", ...);
    });
}
```

**SyncWorker Data Lanes** (for future throttling):

```
Tier: CRITICAL (must succeed, high frequency)
├─ InvoiceSyncJob (produces Neon invoices for Odoo push)
├─ PaymentSyncJob (produces Neon payments for Odoo push)
├─ DeliverySyncJob (produces Neon deliveries for Odoo push)
├─ NeonInvoicePushJob → Odoo
├─ NeonPaymentPushJob → Odoo
└─ NeonDeliveryPushJob → Odoo

Tier: IMPORTANT (business hours priority)
├─ CustomerDeltaSyncJob
├─ SalesOrderSyncJob
├─ NeonCustomerSyncJob
└─ NeonSalesOrderSyncJob

Tier: OPTIONAL (background, catch-up)
├─ ProductFullSyncJob (every 6 hours, enrichment)
├─ PriceListSyncJob (periodic refresh, not blocking)
└─ NeonProductDeltaSyncJob (non-critical inventory)
```

If Neon is overloaded (Tier D+ scenario):
- CRITICAL lane: minimum 30s frequency
- IMPORTANT lane: can batch hourly
- OPTIONAL lane: pause entirely if needed

---

### MolasLubes.ScraperWorker (External Enrichment)

| Aspect | Details | Notes |
|--------|---------|-------|
| **Owns** | Quartz job scheduling for enrichment, web scraping orchestration | Only this service runs enrichment jobs |
| **May Call** | LiquiMolyProductScraper (HTTP calls to external site), MeguinProductScraper, GermaxProductScraper, Cache DB (reads + writes), Neon DB (reads + writes for enriched data) | Patterns: Download data from external site → extract → validate → upsert to Cache/Neon |
| **May NOT** | Create HTTP endpoints, modify transactional data (invoices, payments), call SyncWorker jobs, make SAP changes | Boundary: Scraper ≠ Api, Scraper ≠ SyncWorker |
| **Can Write To** | MolasCacheDbContext (LiquiMolyProducts, AutoHubProducts), NeonDb (LiquiMolyProducts) | Scraper writes are always upserts, never deletes |
| **Can Read From** | Cache (for item code lists), Neon (for existing enriched data) | Read pattern: "what items need enrichment?" |
| **Thread Safety** | Quartz serializes scraper jobs (one at a time); each has scoped DbContext | No parallelism needed (external site would rate-limit anyway) |
| **State Lifetime** | Job-scoped (5-60 minutes typical) | Scraper jobs are long-running by nature |
| **Never Touches** | Transactional tables (Invoices, Payments, Deliveries), SAP, Odoo API, SyncWorker state | If touching these, it violates separation |
| **External Dependencies** | LiquiMoly.com (HTTP), Meguin.com (HTTP), Germax (API) | Subject to rate-limiting, timeouts, site changes |

**Example Valid Pattern**:
```csharp
// LiquiMolyProductScrapeJob.cs
public async Task Execute(IJobExecutionContext context)
{
    // ✅ VALID: Read item codes to scrape
    var itemCodes = await _cacheDb.CacheProducts
        .Select(x => x.ItemCode)
        .Distinct()
        .ToListAsync();
    
    // ✅ VALID: Scrape external site
    var scraped = await _liquiMolyScraper.ScrapeByArticleNumbers(itemCodes);
    
    // ✅ VALID: Upsert enriched data to cache
    foreach (var product in scraped)
        _cacheDb.CacheLiquiMolyProducts.Add(product);
    await _cacheDb.SaveChangesAsync();
    
    // ✅ VALID: Also sync to Neon (enriched data)
    await _liquiMolyNeonSyncService.SyncNeonAsync(scraped);
    
    // ❌ INVALID: Don't modify transactional invoice data
    // _cacheDb.CacheInvoices.Update(...);
    
    // ❌ INVALID: Don't call SAP
    // await _sapProductReader.GetProduct(...);
    
    // ❌ INVALID: Don't push to Odoo
    // await _odooApiClient.UpdateProduct(...);
}
```

---

## Database Write Permissions Grid

| Database | MolasLubes.Api | MolasLubes.SyncWorker | MolasLubes.ScraperWorker |
|----------|---|---|---|
| **MolasCacheDb** | ✅ Write (all tables) | ⚠️ Read-only (for now) | ✅ Write (enrichment tables only: LiquiMolyProducts, AutoHubProducts) |
| **NeonDb** | ⚠️ Reads only (immediate context), no background writes | ✅ Write (all transactional + enrichment tables) | ✅ Write (enrichment tables only: LiquiMolyProducts) |
| **NeonDb:User audit table** (future) | ✅ Write (logged actions) | ❌ Never | ❌ Never |

**Legend**:
- ✅ Yes, intended
- ⚠️ Only under specific conditions
- ❌ Never, architectural boundary

**Rationale for "SyncWorker ⚠️ Cache read-only"**:
- Cache is "write-captured" by Api (from SAP syncs)
- SyncWorker reads Cache to produce Neon state
- Future Tier D: If SyncWorker needs to write Cache (for idempotency), that's a revisit

---

## Dependency Callgraph (What Calls What)

```
HTTP REQUEST FLOW:
┌─────────────────────────────────────────┐
│          MolasLubes.Api                 │
│   (Receives HTTP request)               │
└──────────┬──────────────────┬───────────┘
           │                  │
    ✅ CALLS              ✅ CALLS
           │                  │
           ▼                  ▼
    ┌─────────────┐    ┌─────────────┐
    │   SAP DI    │    │   Cache DB  │
    │  (reads +   │    │   (reads +  │
    │  writes)    │    │   writes)   │
    └─────────────┘    └─────────────┘


SYNC PIPELINE FLOW (Scheduled):
┌────────────────────────────────────┐
│   MolasLubes.SyncWorker            │
│   (Quartz scheduler fires job)     │
└──────────┬────────┬────────────┬───┘
           │        │            │
    ✅     │ ✅     │ ✅         │
    READS  │ READS  │ WRITES     │ ✅ CALLS
           │        │            │
           ▼        ▼            ▼ ▼
        Cache    Neon        Neon  Odoo
         DB      DB           DB    API
      (read)  (read)      (write)


ENRICHMENT FLOW (Nightly):
┌────────────────────────────────────┐
│  MolasLubes.ScraperWorker          │
│  (Quartz scheduler fires job)      │
└──────┬──────────┬─────────────┬────┘
       │          │             │
  ✅   │ ✅       │ ✅          │ ✅
  CALLS│ READS    │ WRITES      │ WRITES
       │          │             │
       ▼          ▼             ▼ ▼
   LiquiMoly  Cache DB       Cache  Neon
   Meguin        (read)        DB    DB
   Germax                    (enrich)


WHAT MUST NEVER HAPPEN:
❌ Api → directly writes Neon (background)
❌ Api → calls Quartz scheduler
❌ SyncWorker → accepts HTTP requests
❌ SyncWorker → makes arbitrary SAP writes
❌ ScraperWorker → modifies invoices/payments
❌ ScraperWorker → calls Odoo API
❌ Any service → circular dependencies
```

---

## Implementation Safety Checklist

When implementing Tier C (service split), verify:

### For MolasLubes.Api

- [ ] All `AddScoped<SapXxxWriter>` registered
- [ ] All `AddScoped<CacheService>` registered
- [ ] All Controllers registered
- [ ] **NO** `AddTransient<NeonXxxSyncJob>` registered
- [ ] **NO** `AddTransient<LiquiMolyProductScrapeJob>` registered
- [ ] **NO** `builder.Services.AddQuartz(...)` registration

### For MolasLubes.SyncWorker

- [ ] All sync job `AddTransient<NeonXxxSyncJob>` registered
- [ ] All sync job `AddTransient<OdooXxxPushJob>` registered
- [ ] `AddScoped<NeonXxxSyncService>` registered (8 of them)
- [ ] **NO** HTTP Controllers registered
- [ ] **NO** SAP writers (only readers)
- [ ] **NO** Scraper registrations

### For MolasLubes.ScraperWorker

- [ ] All scraper Quartz job registrations present (4 of them)
- [ ] Scraper HTTP clients registered (LiquiMoly, Meguin, Germax)
- [ ] **NO** HTTP Controllers registered
- [ ] **NO** Neon sync jobs (transactional syncs only in Worker)
- [ ] **NO** Odoo push jobs
- [ ] **NO** SAP dependencies

---

## Testing Strategy (When Implementing)

### For MolasLubes.Api

**Test**: Verify no Neon background writes
```
// Unit test: Verify no NeonInvoiceSyncService in DI
Assert.Throws<InvalidOperationException>(() => 
    serviceProvider.GetRequiredService<NeonInvoiceSyncService>());
```

### For MolasLubes.SyncWorker

**Test**: Verify Quartz jobs are registered
```
// Integration test: Start SyncWorker, verify jobs exist
var scheduler = serviceProvider.GetRequiredService<IScheduler>();
var jobs = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());
Assert.Contains(nameof(NeonInvoiceSyncJob), jobs.Select(j => j.Name));
```

### For MolasLubes.ScraperWorker

**Test**: Verify no transactional writes
```
// Unit test: Verify NeonInvoiceSyncService NOT in DI
Assert.Throws<InvalidOperationException>(() => 
    serviceProvider.GetRequiredService<NeonInvoiceSyncService>());
```

---

## Future Revisions (When Tier D Starts)

| Change | Trigger | Decision |
|--------|---------|----------|
| Move SAP reads to SyncWorker | If COM stability proven for 24/7 operation | ✅ Revisit, but not now |
| Add message queue between Api and Worker | If async job scheduling needed | ⚠️ Not yet; sync now |
| Split SyncWorker into CRITICAL/OPTIONAL pools | If Neon throttling needed | ⚠️ After Tier A stabilization |
| Add user audit to Api (who changed what) | If compliance required | ⚠️ Future requirement |
| Cache → write-enabled in SyncWorker | If Cache becomes source of sync truth | ❌ Unlikely; Cache is ephemeral |

---

## Summary: Ownership is Explicit Now

**Api**: Synchronous HTTP + SAP writes + Cache management  
**SyncWorker**: Scheduled 3-layer sync (Cache→Neon→Odoo) + data integrity  
**ScraperWorker**: External enrichment (LiquiMoly/Meguin/Germax) + fault-tolerant  

**Each service has**:
- Clear input/output
- Exclusive write domains
- No circular dependencies
- Explicit failure boundaries

**Implementation is now safe**: Tests can verify ownership rules; code reviews can catch violations.

---

**Document Version**: 1.0  
**Status**: Reference for Tier C implementation safety  
**Last Updated**: April 4, 2026
