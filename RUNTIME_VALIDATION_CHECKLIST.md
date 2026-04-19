# Neon Connection Stability Fixes — Runtime Validation Checklist
**Status**: Implementation Complete, Awaiting Runtime Confirmation  
**Deployed**: April 5, 2026  
**Validation Period**: 48 hours minimum (one full business cycle + 24h overflow)  
**Success Criteria**: All four operational signals healthy for 24h consecutive

---

## Three Fixes Under Observation

| Fix | Component | Expected Signal | Watch Duration |
|-----|-----------|-----------------|-----------------|
| **#1** | PriceListNeonSyncService transaction | Transaction duration stability | 48h |
| **#2** | Neon connection hardening | Zero auth/connection exceptions | 48h |
| **#3** | Schedule pressure reduction | Queue depth stability | 48h |

---

## Validation Strategy

### Phase 1: Pre-Flight Checks (0-30 min after deployment)

**Objective**: Confirm code loaded without errors; basic connectivity works

```
☐ Application startup succeeded (no compilation/schema errors)
☐ DI container initialized (all DbContexts registered)
☐ First sync job fired successfully (check logs for "sync started" message)
☐ Neon connection pool established (check "Connected successfully" log)
☐ Odoo push jobs registered in Quartz (verify job keys exist)
```

**Success Criteria**: All checks pass; no exceptions in first 5 minutes of logs

**Log Keyword Search**:
```
✓ "Application started"
✓ "Neon PRICE LIST sync started"
✓ "Connected successfully to Neon"
✓ "Quartz scheduler started"
✗ "NpgsqlException"
✗ "connection forcibly closed"
✗ "Cannot connect"
```

---

### Phase 2: Operational Signal Monitoring (0-48 hours)

#### Signal #1: Transaction Duration Stability

**What to Watch**: How long PriceListNeonSyncJob actually takes end-to-end

**Log Pattern**:
```
[INFO] 💰 Neon PRICE LIST sync started
[INFO] ✅ Neon PRICE LIST sync completed | Rows=1245
```

**Measure**: Duration between "started" and "completed" log entries

**Expected Behavior** (Target Window):
| Phase | Expected Time | Max Acceptable |
|-------|---------------|---|
| Cache read + expand | 100-300ms | 500ms |
| Neon read (existing) | 100-200ms | 500ms |
| Transaction open + upsert + commit | 50-150ms | 250ms |
| **Total job duration** | **250-650ms** | **1500ms** |

**Previous Behavior** (Baseline for Comparison):
- Total job duration: 2-8 seconds (with occasional connection timeouts)
- Failure rate: ~5-10% intermittent

**Go/No-Go Threshold**:
- ✅ **GO**: Average job duration < 1.0 second for 24h
- ✅ **GO**: P95 job duration < 1.5 seconds
- ❌ **NO-GO**: Any job duration > 5 seconds (indicates regression)
- ❌ **NO-GO**: Frequent (>10% of runs) achieving duration > 2 seconds

**Collection Method** (Production Logging):

Add timing instrumentation in logs (if not already present):
```csharp
var sw = Stopwatch.StartNew();

// Phase 1: Cache read
sw.Checkpoint("cache_read");

// Phase 2: Neon read
sw.Checkpoint("neon_read");

// Phase 3: Transaction
sw.Checkpoint("tx_start");
// ... upsert ...
sw.Checkpoint("tx_commit");

_logger.LogInformation(
    "💰 Neon PRICE LIST sync completed | Rows={Rows} | " +
    "CacheReadMs={CacheReadMs} NeonReadMs={NeonReadMs} TxMs={TxMs} TotalMs={TotalMs}",
    prices.Count,
    sw.GetElapsedMs("cache_read"),
    sw.GetElapsedMs("neon_read"),
    sw.GetElapsedMs("tx_start", "tx_commit"),
    sw.ElapsedMilliseconds);
```

**Dashboard Metric** (if available):
- Grafana: `histogram_quantile(0.95, rate(price_list_sync_duration_ms[5m]))`
- Target: P95 < 1500ms for 24h

---

#### Signal #2: Exception Frequency (Neon/Connection Errors)

**What to Watch**: Exceptions that indicate connection/auth problems

**Critical Exception Search Terms**:
```
"NpgsqlException"           ← All Npgsql failures
"connection forcibly closed" ← Remote disconnect
"Connection timeout"        ← Pool exhausted or Neon slow
"authentication failed"     ← Auth failure (should be 0 after fix)
"SSL"                       ← Certificate issues
"pool is full"              ← Connection pool exhaustion
```

**Expected Behavior** (Target):
- **Zero** authentication exceptions (fix #2 eliminated malformed password)
- **Zero** "connection forcibly closed" during normal sync operations
- **Baseline** (transient network hiccups): <0.1% of all queries
- **Acceptable**: 1 timeout per 10,000 queries (retry should handle)

**Previous Behavior** (Baseline):
- "Connection forcibly closed": 3-5 per day (during price list sync)
- Authentication: sporadic failures
- Intermittent timeouts: 1-2 per shift

**Go/No-Go Threshold**:
- ✅ **GO**: Zero "connection forcibly closed" errors in 24h
- ✅ **GO**: Zero "authentication failed" errors in 24h
- ✅ **GO**: NpgsqlException rate < 0.05% (1 per 2000 queries)
- ❌ **NO-GO**: Any "connection forcibly closed" during sync jobs
- ❌ **NO-GO**: Recurring "authentication failed" (pattern, not one-off)

**Collection Method** (Log Analysis):

```bash
# Count exception occurrences over observation window
grep -i "npsqlexception\|connection forcibly closed\|authentication failed" logs/*.log | wc -l

# Expected output after 48h: 0 (or <5 for entire 48h window)
```

**Expected Log Signature** (Normal, Healthy):
```
[INFO] 💰 Neon PRICE LIST sync started
[INFO] ✅ Neon PRICE LIST sync completed | Rows=1245 | TotalMs=387
[INFO] 🧾 Neon INVOICE DELTA sync started
[INFO] ✅ Neon INVOICE DELTA sync completed | Headers=42 Lines=156 | TotalMs=521
[INFO] 🧾 Neon INVOICE DELTA sync started
[INFO] ✅ Neon INVOICE DELTA sync completed | Headers=15 Lines=43 | TotalMs=294
```

**Error Signature** (Problem):
```
[ERROR] 💰 Neon PRICE LIST sync started
[ERROR] NpgsqlException: Exception while reading from stream
[ERROR] SqlNullValueException: Data is Null
[ERROR] The connection was forcibly closed by the remote host
```

If you see the error signature: **STOP** and investigate (do not proceed as "stable")

---

#### Signal #3: Job Queue Depth Stability

**What to Watch**: How many jobs are waiting to execute (queue backlog)

**Expected Behavior** (Schedule Pressure Reduction):
| Time Window | Expected Queue Depth | Max Acceptable |
|------|------|---|
| Normal operations | 0-1 (mostly 0) | 2 jobs |
| During incoming spike | 1-2 (temporary) | 3 jobs |
| Sustained load | 1 jobs | 2 jobs |

**Previous Behavior** (Baseline):
- Normal: queue depth 1-3 (jobs stacked)
- During delivery batch: queue depth 3-5
- Recovery time: 30-60 seconds to clear queue

**Go/No-Go Threshold**:
- ✅ **GO**: Average queue depth 0.5 or lower over 24h
- ✅ **GO**: P95 queue depth ≤ 2 jobs
- ✅ **GO**: Queue clears to 0 within 15 seconds after incoming spike
- ❌ **NO-GO**: Average queue depth > 1.5 for sustained period (>10 min)
- ❌ **NO-GO**: Queue depth ever reaches 5+ jobs consistently

**Collection Method** (Quartz Scheduler Inspection):

```csharp
// In a monitoring endpoint or scheduled check job:
var scheduler = serviceProvider.GetRequiredService<IScheduler>();
var executingJobs = await scheduler.GetCurrentlyExecutingJobs();
var scheduledJobs = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());

var queueDepth = scheduledJobs.Count - executingJobs.Count;
_logger.LogInformation("📊 Quartz Queue Depth: {QueueDepth} jobs waiting", queueDepth);
```

**Alternative** (If Quartz monitoring not available):
Count job execution frequency in logs:
```bash
# Extract "sync started" log entries with timestamps
grep "sync started" logs/*.log | wc -l

# Should show roughly steady rate:
# ~15-20 jobs per minute under schedule #3 (reduced from 9 to 6 per 10s)
# Should NOT show clustering or gaps indicating backlog
```

**Expected Log Pattern** (Healthy Queue Depth):
```
[INFO] 🧾 Neon INVOICE DELTA sync started     ← Sec 4
[INFO] ✅ Neon INVOICE DELTA sync completed   ← Sec 4 + 500ms
[INFO] 💰 Neon PRICE LIST sync started        ← Sec 10
[INFO] ✅ Neon PRICE LIST sync completed      ← Sec 10 + 400ms
[INFO] 🧾 Neon INVOICE DELTA sync started     ← Sec 14
[INFO] ✅ Neon INVOICE DELTA sync completed   ← Sec 14 + 520ms
```

**Problem Pattern** (High Queue Depth):
```
[INFO] 🧾 Neon INVOICE DELTA sync started     ← Sec 4
[INFO] 🎁 Neon DELIVERY sync started          ← Sec 5 (shouldn't start if previous still running)
[INFO] 💳 Neon PAYMENT sync started           ← Sec 6
[INFO] 📦 Neon ORDER sync started             ← Sec 7 (all queued, none executed yet)
[ERROR] Job execution timeout (queue is saturated)
```

If you see the problem pattern: investigate why jobs aren't completing fast enough

---

#### Signal #4: Odoo Sync Latency (Downstream Consumer Impact)

**What to Watch**: Are Odoo pushes falling behind after schedule change?

**Expected Behavior**:
- Odoo push jobs fire at 15s interval (vs previous 10s)
- Odoo API receives data within 20-30 seconds of Neon write
- No customer complaints about "stale" data in Odoo

**Previous Behavior** (Baseline):
- Odoo push @ 10s interval
- Odoo API latency: 10-20s end-to-end
- Occasional: "invoice appeared in Odoo 60+ seconds after SAP entry"

**Go/No-Go Threshold**:
- ✅ **GO**: End-to-end latency (invoice in SAP → appears in Odoo): ≤ 45 seconds (was ~15-20s)
- ✅ **GO**: No customer complaints about "missing" or "delayed" invoices
- ✅ **GO**: Odoo API success rate ≥ 95% (retry + 15s interval maintain throughput)
- ❌ **NO-GO**: End-to-end latency consistently > 60 seconds (indicates bottleneck elsewhere)
- ❌ **NO-GO**: Odoo API failures becoming more frequent (indicates new pressure point)

**Collection Method** (Trace SAP → Odoo Journey):

1. **SAP Invoice Created** (timestamp from SapInvoiceReader)
   ```
   [INFO] 📝 SAP Invoice Entry 12345 cached at 2026-04-05T14:32:15.123Z
   ```

2. **Neon Invoice Synced** (timestamp from NeonInvoiceSyncJob)
   ```
   [INFO] ✅ Neon INVOICE DELTA sync completed | Entry 12345 synced at 2026-04-05T14:32:21.456Z
   ```

3. **Odoo Push Attempt** (timestamp from OdooInvoicePushJob)
   ```
   [INFO] 🚀 Odoo Invoice Push: Entry 12345 → OdooId 67890 at 2026-04-05T14:32:36.789Z
   ```

**Total Latency**: 36.789 - 15.123 = **21.666 seconds** ✅ (well under 45s target)

**Track via**: Extract timestamps for a sample of 50 invoices over 24h; calculate percentiles
- P50 (median): should be 15-30s
- P95: should be < 45s
- P99: acceptable up to 60s

---

### Phase 3: Aggregated Health Check (24-hour Review)

**Objective**: Confirm all four signals are healthy; make go/no-go decision

**Checklist**:
```
🔍 SIGNAL #1: Transaction Duration
  ☐ Average job time < 1.0s
  ☐ P95 < 1.5s
  ☐ No jobs exceeded 5s threshold
  ☐ Conclusion: [PASS / PASS-WITH-NOTES / FAIL]

🔍 SIGNAL #2: Exception Frequency  
  ☐ Zero "connection forcibly closed" errors
  ☐ Zero "authentication failed" errors
  ☐ NpgsqlException rate < 0.05%
  ☐ Conclusion: [PASS / PASS-WITH-NOTES / FAIL]

🔍 SIGNAL #3: Queue Depth Stability
  ☐ Average queue depth ≤ 0.5
  ☐ P95 queue depth ≤ 2
  ☐ Queue clears within 15s after spike
  ☐ Conclusion: [PASS / PASS-WITH-NOTES / FAIL]

🔍 SIGNAL #4: Odoo Latency Impact
  ☐ End-to-end SAP→Odoo latency < 45s (P95)
  ☐ No customer complaints about delays
  ☐ Odoo API success rate ≥ 95%
  ☐ Conclusion: [PASS / PASS-WITH-NOTES / FAIL]
```

**Final Decision Frame**:
- **PASS**: All four signals PASS → Deploy to production, monitor weekly
- **PASS-WITH-NOTES**: 1-2 signals have minor findings (acceptable trade-off) → Deploy, set up weekly review
- **FAIL**: Any signal FAIL or > 2 PASS-WITH-NOTES → Rollback, investigate

---

## Monitoring Setup Required

### Log File Locations
- Application logs: `C:\logs\MolasLubes.Api\*.log` (or equivalent)
- Quartz scheduler: same log file (Quartz logs to application logger)
- Neon connection: Npgsql logs mixed into application logs

### Log Aggregation (Recommended)
If using centralized logging (ELK/Splunk):
```
Index: molaslubes_api
Filter: timestamp >= now()-48h
Search:
  "sync started" OR "sync completed" OR 
  "NpgsqlException" OR "connection forcibly closed" OR
  "authentication failed"
```

### Key Queries by Signal

**Signal #1 - Transaction Duration**:
```
fields _time, message | 
search "PRICE LIST sync completed" | 
stats avg(duration) as avg_duration, max(duration) as max_duration by status
```

**Signal #2 - Exception Frequency**:
```
search "NpgsqlException" OR "connection forcibly closed" OR "authentication failed" |
stats count as exception_count, values(exception_type) as types
```

**Signal #3 - Queue Depth**:
```
search "Queue Depth" |
stats avg(queue_depth) as avg, max(queue_depth) as max, 
      latest(queue_depth) as current by time
```

**Signal #4 - Odoo Latency**:
```
search "Odoo Invoice Push" |
stats avg(push_latency_ms) as avg_latency, 
      pct(push_latency_ms, 95) as p95_latency
```

---

## Rollback Decision Tree

**If during 48h validation you see any of these:

```
Signal #1: Regression
  - Average job duration > 2.0s consistently
  - P95 job duration > 3.0s
  → Action: INVESTIGATE (don't rollback yet; check if other jobs are slow)
  → If isolated to PriceListNeonSyncService: ROLLBACK FIX #1

Signal #2: Regression  
  - Any "connection forcibly closed" error
  - Recurring "authentication failed"
  → Action: IMMEDIATE INVESTIGATION
  → Check: connection string parsing, Neon cluster status
  → If not infrastructure: ROLLBACK FIX #2

Signal #3: Regression
  - Queue depth consistently > 3.0
  - Queue takes > 60s to clear after spike
  → Action: INVESTIGATE (could be Neon bottleneck, not schedule)
  → If confirmed schedule issue: ROLLBACK FIX #3

Signal #4: Regression
  - End-to-end latency consistently > 60s
  - Odoo API failure rate > 10%
  → Action: INVESTIGATE (Odoo API or network issue?)
  → If confirmed to be schedule change: Revert Odoo job intervals to 10s
```

**Rollback Procedure** (if needed):
```bash
# Git revert specific commits
git revert <commit-id>        # Reverts PriceListNeonSyncService fix
git revert <commit-id>        # Reverts connection string changes
git revert <commit-id>        # Reverts schedule changes

# Or manual rollback:
# - Restore previous appsettings.json (git checkout HEAD~1 appsettings.json)
# - Restore previous Program.cs schedule (git checkout HEAD~1 Program.cs)
# - Restore previous PriceListNeonSyncService.cs (git checkout HEAD~1 ...)

# Rebuild and redeploy
dotnet build -c Release
# Deploy using existing CI/CD pipeline
```

**Post-Rollback**:
- Monitor for 2 hours to confirm previous issues return (expected)
- Document what went wrong
- Plan next iteration (e.g., different schedule interval, different transaction pattern)

---

## Success Criteria Summary

**All Four Signals Must Be Healthy for 24 Consecutive Hours**:

| Signal | Target | Accept | Red Flag |
|--------|--------|--------|----------|
| Txn Duration (P95) | <1.0s | <1.5s | >3.0s |
| Exceptions | 0 | <5/day | >5 recurring |
| Queue Depth (avg) | <0.3 | <0.5 | >1.5 |
| Odoo Latency (P95) | <30s | <45s | >60s |

**Go/No-Go Timeline**:
- T+0h: Deploy, watch Phase 1 checks (30 min)
- T+6h: Spot check all four signals (should look good)
- T+24h: First formal review (all signals healthy?)
- T+48h: Final sign-off review (sustained health confirmed?)

---

## Communication Checklist

**Before Deployment**:
- [ ] Operations team notified (expect monitoring in Quartz logs)
- [ ] Odoo sync consumers notified (schedule change may affect latency ±5s)
- [ ] Escalation contact identified (who to call if something breaks)

**During 48h Validation**:
- [ ] Daily standup: signal health summary (3-4 sentences)
- [ ] Any anomaly: immediate Slack notification (don't wait for standup)

**After 48h (Approval/Rollback)**:
- [ ] Written validation report (one page: signals, findings, decision)
- [ ] Stakeholder sign-off (ops lead, Odoo integration owner)
- [ ] Update runbook (if staying deployed: add to monitoring checklist)

---

**Validation Started**: April 5, 2026, 14:00 UTC  
**Expected Completion**: April 7, 2026, 14:00 UTC  
**Decision Point**: April 7, 2026, 16:00 UTC  

Status: ⏳ IN PROGRESS — Awaiting Runtime Data
