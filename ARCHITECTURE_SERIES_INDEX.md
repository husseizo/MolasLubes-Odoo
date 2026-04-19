# MolasLubes Architecture Series: Three-Document Guide

**Quick Reference**: Use these three documents together to safely implement the Tier C service split (MolasLubes.Api → 3 independent services).

---

## The Three Documents

### 1. **OWNERSHIP_MATRIX.md** — "Who Owns What?"
**Purpose**: Explicit ownership grids for each service  
**When to use**: 
- Designing new features (which service? where does data live?)
- Code review (does this service have permission to do this?)
- Planning DI container registration
- Defining database write permissions

**Key sections**:
- Service Ownership Grid (what each service owns)
- Database Write Permissions Grid (who can write where?)
- Dependency Callgraph (visual: what calls what)
- Implementation Safety Checklist (pre-deployment verification)
- Testing Strategy (DI container validation tests)

**Example usage**: "Can SyncWorker write to MolasCacheDb?"  
→ Check OWNERSHIP_MATRIX.md Line 53: ⚠️ Read-only for now

---

### 2. **TIER_ROADMAP.md** — "How Do We Get There?"
**Purpose**: Staged progression from Tier A (single service) → Tier D (advanced ops)  
**When to use**:
- Planning the implementation timeline
- Understanding risk levels at each stage
- Checking exit criteria before advancing to next tier
- Rollback decision-making

**Key sections**:
- Tier A: Current state (monolithic Api)
- Tier B: SyncWorker as in-process DLL (low risk)
- Tier C: Physical split (three services, shared DBs)
- Tier D: Advanced ops (throttling by lane, Redis caching)
- Decision tree (when to advance)
- Rollback plan (how to undo if things go wrong)

**Example usage**: "We're ready to split. What's Tier B exactly?"  
→ Check TIER_ROADMAP.md Line 80: In-process DLL + Quartz RAM store for 7 days

---

### 3. **SERVICE_BOUNDARY_REVIEW.md** — "What Breaks If We Get This Wrong?"
**Purpose**: Operational guardrails and architectural violations  
**When to use**:
- Code review (checking for architectural violations)
- Developer onboarding (here's how to keep services separate)
- DI container startup verification
- PR approval checklist

**Key sections**:
- Current state breakdown (what's in each service today)
- Service 1-3 detailed responsibilities (SAP readers, sync jobs, scrapers)
- 7 Common Violations (with code examples and fixes)
- Per-service DI verification tests
- Code review checklist

**Example usage**: "Is this SyncWorker code correct?"  
→ Check SERVICE_BOUNDARY_REVIEW.md: VIOLATION #2 (SyncWorker can't create invoice data)  
→ Compare code pattern to Correct Pattern provided

---

## How to Use All Three Together

### Scenario 1: Designing a New Sync Job (e.g., "NeonProductSyncJob")

1. **Check TIER_ROADMAP.md** (Line 150)
   - Is ProductSync in CRITICAL or OPTIONAL tier?
   - What's the schedule frequency?
   - What's the rollback gate?

2. **Check OWNERSHIP_MATRIX.md** (Line 20-80)
   - Can SyncWorker read from Cache?
   - Can SyncWorker write to Neon?
   - Should it call Odoo API?

3. **Check SERVICE_BOUNDARY_REVIEW.md** (Line 210-280)
   - Review VIOLATION #2 (SyncWorker creating data)
   - Verify your job reads from Cache, transforms, writes to Neon
   - Don't create products — sync existing ones

**Result**: Write NeonProductSyncJob with confidence

---

### Scenario 2: Code Review — Checking a PR from a Developer

1. **Read the PR code**
   - Identify which service(s) it touches
   - Identify database operations (reads/writes)

2. **Check SERVICE_BOUNDARY_REVIEW.md** (Line 180-350)
   - Run through the 7 Common Violations
   - Does the code match any pattern?
   - Are there red flags?

3. **Run the DI verification test** (Line 350-400)
   - Does the service have correct dependencies?
   - Does it have forbidden dependencies?

4. **Use the Code Review Checklist** (Line 410-420)
   - Did the developer grep-check for violations?
   - Are unit tests passing?

**Result**: Approve or request changes with specific references

---

### Scenario 3: Implementing Tier C (Full Service Split)

**Phase 1: Planning**
- Check TIER_ROADMAP.md (Line 120-160) for Tier B exit criteria
- Check TIER_ROADMAP.md (Line 160-220) for Tier C architecture diagram
- Check OWNERSHIP_MATRIX.md (entire document) for service boundaries

**Phase 2: Implementation**
- Create SyncWorker.csproj with jobs in SERVICE_BOUNDARY_REVIEW.md (Line 60-150)
- Register services per OWNERSHIP_MATRIX.md (Line 95-130)
- Add DI verification tests per SERVICE_BOUNDARY_REVIEW.md (Line 350-400)

**Phase 3: Validation**
- Run DI container startup tests
- Run code review checklist (SERVICE_BOUNDARY_REVIEW.md Line 410-420)
- Verify exit criteria (TIER_ROADMAP.md Line 180-200)

**Phase 4: Deployment**
- Follow TIER_ROADMAP.md deployment order (Line 230-250)
- Monitor per TIER_ROADMAP.md success metrics (Line 210-230)
- Prepare rollback plan per TIER_ROADMAP.md (Line 320-360)

---

## Quick Reference: Service Decision Matrix

**Question**: Can Service X do Y?

| Question | Check This | Location |
|----------|-----------|----------|
| Can SyncWorker read from Cache? | OWNERSHIP_MATRIX.md | Line 53 (⚠️ Read-only) |
| Can Api write to Neon? | SERVICE_BOUNDARY_REVIEW.md | Line 180 (❌ VIOLATION #1) |
| Should ScraperWorker push to Odoo? | SERVICE_BOUNDARY_REVIEW.md | Line 270 (❌ VIOLATION #7) |
| What's the schedule for ProductSync? | TIER_ROADMAP.md | Line 150 (IMPORTANT tier) |
| What services does Api have? | OWNERSHIP_MATRIX.md | Line 20-50 |
| How do we rollback Tier C? | TIER_ROADMAP.md | Line 320 (best-case: 4 hours) |
| Is this a circular dependency? | SERVICE_BOUNDARY_REVIEW.md | Line 250 (❌ VIOLATION #6) |
| When can we move to Tier D? | TIER_ROADMAP.md | Line 300 (30-day stability) |

---

## Document Progression: Use Them in This Order

### First Time Learning About the Architecture
1. Read TIER_ROADMAP.md (understand the stages)
2. Read OWNERSHIP_MATRIX.md (understand the boundaries)
3. Read SERVICE_BOUNDARY_REVIEW.md intro (understand violations)

### Implementing Tier C
1. Check TIER_ROADMAP.md exit criteria for Tier B ← "Are we ready?"
2. Follow TIER_ROADMAP.md Tier C architecture ← "What do we build?"
3. Check OWNERSHIP_MATRIX.md for each service ← "What goes where?"
4. Use SERVICE_BOUNDARY_REVIEW.md for code patterns ← "How do we code it?"
5. Run SERVICE_BOUNDARY_REVIEW.md tests ← "Did we succeed?"

### Code Review During Development
1. Check SERVICE_BOUNDARY_REVIEW.md violations ← "Does it break any rules?"
2. Check OWNERSHIP_MATRIX.md permissions ← "Is this service allowed?"
3. Run DI tests from SERVICE_BOUNDARY_REVIEW.md ← "Does registration work?"
4. Use code review checklist (Line 410) ← "Final questions?"

### Operational Runbooks (Later)
1. Check TIER_ROADMAP.md graceful shutdown (Line 250)
2. Check TIER_ROADMAP.md rollback plan (Line 320)
3. Check OWNERSHIP_MATRIX.md failure boundaries (Line 130)

---

## Key Insights from All Three Documents

### From OWNERSHIP_MATRIX.md
- Each service has exclusive write domains (no conflicts)
- SyncWorker is read-only on Cache (by design)
- Api never touches Neon directly (async boundary)
- All three services are independently debuggable

### From TIER_ROADMAP.md
- Tier C is the "physically split" stage (three .exe files)
- Tier B is lower-risk practise run (same process, different logic)
- Exit gates are clear (7 days stable, metrics verified, tests passing)
- Rollback time increases per tier (2h for B → 4h for C → 30min for D)

### From SERVICE_BOUNDARY_REVIEW.md
- 7 specific violation patterns to avoid
- Each violation has a code example + fix
- DI container tests enforce boundaries at startup
- Code review checklist is objective (greppable)

---

## Success Criteria: By Document

**OWNERSHIP_MATRIX ensures**:
- ✅ Each service writes to correct database only
- ✅ No circular dependencies
- ✅ Clear failure boundaries
- ✅ Independent restart capability

**TIER_ROADMAP ensures**:
- ✅ Staged risk reduction (no big-bang split)
- ✅ Clear go/no-go gates between tiers
- ✅ Rollback paths defined
- ✅ Observability targets specified

**SERVICE_BOUNDARY_REVIEW ensures**:
- ✅ Common mistakes prevented by code pattern examples
- ✅ DI container validates boundaries at startup
- ✅ Code review has objective criteria
- ✅ Developers can self-check via violation list

---

## When One Document Isn't Enough

**Issue**: "Should SyncWorker write to Cache?"  
**Answer Chain**:
1. Check OWNERSHIP_MATRIX.md Line 53: "⚠️ Read-only (for now)"
2. Check TIER_ROADMAP.md Line 410: "Tier D might change this"
3. Check SERVICE_BOUNDARY_REVIEW.md Line 220: "It would violate SyncWorker's single job"

**Conclusion**: No, not now. Revisit in Tier D if needed.

---

**How to Use These Docs**:
1. **Save all three** in the repo root (not nested)
2. **Link them in code comments** when boundary decisions matter
   ```csharp
   // See OWNERSHIP_MATRIX.md Line 53: SyncWorker reads Cache (read-only)
   var changeSet = await _cacheDb.Invoices.Where(...).ToListAsync();
   ```
3. **Cite them in PR reviews** when rejecting boundary violations
   ```
   ❌ This violates SERVICE_BOUNDARY_REVIEW.md VIOLATION #2.
   SyncWorker must not create invoice data; it should transform cached data.
   See line 210 for the correct pattern.
   ```
4. **Include them in code review checklists**
   ```
   [ ] Read SERVICE_BOUNDARY_REVIEW.md violations list
   [ ] Run DI verification tests (Line 350)
   [ ] Check code review checklist (Line 410)
   ```

---

**Collection**: MolasLubes Service Architecture Reference  
**Status**: Complete (3 documents, 4,800+ lines combined)  
**Last Updated**: April 4, 2026  
**Revision**: 1.0
