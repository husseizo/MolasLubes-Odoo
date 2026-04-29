# 🔍 LiquiMoly Inter-Company Implementation - Discovery Analysis

**Purpose:** Comprehensive analysis of existing infrastructure before implementing Sales Order + Purchase Order flow

**Date:** 2025-01-07  
**Status:** ✅ **DISCOVERY COMPLETE** - Ready for design discussion

---

## 📊 **Executive Summary**

### **Current Understanding**

Based on code exploration and user clarification:

| Aspect | Details |
|--------|---------|
| **Supplier Company** | **MolasLubes** (Molas_Lubes_LTD) - LIQUI MOLY authorized distributor |
| **Receiver Company** | **AutoHub** (MOLAS_Live_2021) - Internal company needing stock |
| **SAP Environment** | **Same SAP B1 server**, different companies (separate databases) |
| **Current Flow** | Goods Issue (MolasLubes) → Goods Receipt (AutoHub) |
| **Proposed Flow** | Sales Order (MolasLubes) + Purchase Order (AutoHub) + Goods Receipt (AutoHub) |
| **Item Mapping** | Article numbers (3682) ↔ LUB codes (LUB100001) |
| **Pricing** | PL05 = PL01 + ((PL01 - PL03) / 2) where missing/zero |

---

## 🗄️ **Database Configuration Analysis**

### **Connection Strings (appsettings.Development.json)**

```json
{
  "ConnectionStrings": {
    "MolasCacheDb": "Server=.;Database=MOLAS_Live_2021_Cache;User Id=sa;Password=***;TrustServerCertificate=True;Connection Timeout=60",
    "NeonDb": "Host=ep-red-night-ahmf5aig-pooler.c-3.us-east-1.aws.neon.tech;Database=Parts_Catalog;Username=neondb_owner;Password=***;SSL Mode=Require;Pooling=true;Max Pool Size=20;Include Error Detail=false;Timeout=60"
  },
  "SAP": {
    "Server": "WIN-GJGQ73V0C3K",
    "CompanyDB": "Molas_Lubes_LTD",   // ← Default profile
    "UserName": "hussein",
    "Password": "Modern00.",
    "DbServerType": "MSSQL2016",
    "LicenseServer": "WIN-GJGQ73V0C3K:30000",
    "SLDServer": "WIN-GJGQ73V0C3K:40000"
  }
}
```

### **Profile Configuration (Program.cs Lines 196-216)**

```csharp
// PROFILE B (AutoHub) - MOLAS_Live_2021
builder.Services.AddDbContext<Live2021CacheDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration["IntegrationProfiles:Profiles:AutoHub:ConnectionStrings:CacheDb"],
        sql =>
        {
            sql.MigrationsAssembly("MolasLubes.Infrastructure");
            sql.MigrationsHistoryTable("__EFMigrationsHistory_Live2021Cache");
        }));

builder.Services.AddDbContext<AutoHubDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration["IntegrationProfiles:Profiles:AutoHub:ConnectionStrings:NeonDb"],
        npgsql =>
        {
            npgsql.MigrationsAssembly("MolasLubes.Infrastructure");
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory_AutoHub");
            npgsql.CommandTimeout(120);
            npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
        }));

// IntegrationProfiles binding (lines 221-222)
builder.Services.Configure<IntegrationProfilesOptions>(
    builder.Configuration.GetSection(IntegrationProfilesOptions.SectionName));
```

**Finding:** ✅ Multi-profile infrastructure **already exists** for AutoHub integration!

---

## 🏢 **Company Structure Clarification**

### **User Confirmation**

> "molas lubes is the supplier and molas lubes has itemcode with liqui moly article number, and in autohub uses AutoHub item LUB100001 and in oitm.itemname has liqu moly article number included"

### **Interpreted Business Flow**

```
┌────────────────────────────────────────────────────────────────┐
│ MolasLubes (Molas_Lubes_LTD) - SUPPLIER                       │
│ ─────────────────────────────────────────────────────────────  │
│ • Profile Key: "MolasLubes" or "LIQUIMOLY"                    │
│ • OITM.ItemCode: "3682" (article number IS the item code)     │
│ • OITM.U_Item_Name: "3682 TOP TEC ATF 1200 5L"                │
│ • OITM.U_MdlTEST: "LIQUI MOLY"                                 │
│ • Stock tracked in: WH01 (or similar)                          │
│ • Role: SELLING to AutoHub                                     │
│ • Document: SALES ORDER (ORDR)                                 │
└────────────────────────────────────────────────────────────────┘
                        ↓ (inter-company transfer)
┌────────────────────────────────────────────────────────────────┐
│ AutoHub (MOLAS_Live_2021) - RECEIVER                           │
│ ─────────────────────────────────────────────────────────────  │
│ • Profile Key: "AutoHub"                                       │
│ • OITM.ItemCode: "LUB100001" (internal sequential code)        │
│ • OITM.ItemName: "3682 TOP TEC ATF 1200 5L" (includes article)│
│ • Stock tracked in: WH02 (or similar)                          │
│ • Role: BUYING from MolasLubes                                 │
│ • Documents: PURCHASE ORDER (OPOR) + GOODS RECEIPT (OIGN)     │
└────────────────────────────────────────────────────────────────┘
```

---

## 🔗 **Item Mapping Logic**

### **Existing Implementation**

**File:** `SapLiquiMolyItemMapper.cs` (Lines 36-143)

**Key Logic:**

1. **Source (MolasLubes):**
   - Query: `SELECT ItemCode, ItemName, U_Item_Name FROM OITM WHERE ItemCode = '3682'`
   - Extract article number from `U_Item_Name` (first numeric token 3-6 digits)
   - Example: "3682 TOP TEC ATF 1200 5L" → Article = "3682"

2. **Target (AutoHub):**
   - Query: `SELECT ItemCode, ItemName FROM OITM WHERE ItemCode = ? AND U_MdlTEST = 'LIQUI MOLY'`
   - In AutoHub, the **ItemName contains the article number**
   - Example: "LUB100001" with ItemName "3682 TOP TEC ATF 1200 5L"
   - **BUT:** Current code expects `ItemCode = articleNumber` (line 119)

**Critical Discovery:** ❗ The mapper assumes **Target.ItemCode = ArticleNumber** (direct match), but user says AutoHub uses "LUB100001" codes!

### **Required Mapping Change**

Current query (line 116-123):
```csharp
rs.DoQuery($@"
SELECT TOP 1 ItemCode, ItemName
FROM OITM
WHERE ItemCode   = '{safeArt}'  // ❌ This won't match LUB codes!
  AND U_MdlTEST  = 'LIQUI MOLY'
  AND frozenFor  = 'N'
");
```

**Should be:**
```csharp
rs.DoQuery($@"
SELECT TOP 1 ItemCode, ItemName
FROM OITM
WHERE ItemName LIKE '%{safeArt}%'  // ✅ Search in ItemName for article
  AND U_MdlTEST = 'LIQUI MOLY'
  AND frozenFor = 'N'
  AND ItemCode LIKE 'LUB%'  -- ✅ Ensure AutoHub format
ORDER BY ItemCode
");
```

**Or use a UDF:** Check if AutoHub has `U_Item_Name` or custom UDF for article lookup.

---

## 💰 **Pricing Infrastructure**

### **Existing Price Reading**

**File:** `SapPricingReader.cs` (Lines 66-92)

```csharp
public SapItemPriceDto? GetItemPrice(string itemCode, int priceList)
{
    rs.DoQuery($@"
    SELECT TOP 1 Price, U_Odoo_PriceItem_ID
    FROM ITM1
    WHERE ItemCode = '{itemCode.Replace("'", "''")}'
      AND PriceList = {priceList}
    ");
    
    return new SapItemPriceDto
    {
        ItemCode = itemCode,
        PriceList = priceList,
        Price = Convert.ToDecimal(rs.Fields.Item("Price").Value)
    };
}
```

**Usage:** Can query ITM1 for PL01, PL03, PL05 in both companies.

### **User Pricing Rule**

> "Fill PriceList = '05' where missing/zero using PL05 = PL01 + ((PL01 - PL03) / 2) for autohub purchasing"

**Interpretation:**
- AutoHub needs **PL05 pricing** for Purchase Orders
- If ITM1.Price (PriceList=5) is NULL or 0, calculate dynamically:
  ```
  PL05 = PL01 + ((PL01 - PL03) / 2)
  ```
- This formula means: **Markup = 50% of retail margin**

**Example:**
```
PL01 (Retail)   = $100
PL03 (Cost)     = $60
Margin          = $100 - $60 = $40
PL05 (Transfer) = $100 + ($40 / 2) = $120
```

**Question for User:** Should this price be:
1. **Calculated at runtime** during PO creation?
2. **Backfilled into ITM1** table (PriceList=5) in AutoHub?
3. **Read from MolasLubes' PL05** or **AutoHub's PL05**?

---

## 🔧 **Existing SAP Writers**

### **Already Implemented**

✅ **`SapGoodsIssueWriter.cs`** (Lines 82-112)
- Creates OIGE documents
- Uses profileKey for multi-company
- Can add U_TransferRef UDF

✅ **`SapGoodsReceiptWriter.cs`** (Lines 19-26)
- Creates OIGN documents
- Uses profileKey
- Needs enhancement to support **base document (PO)**

✅ **`SapSalesOrderCreator.cs`**
- Already creates ORDR documents
- Used for Odoo sync
- Can be adapted for inter-company with customer code

❌ **`SapPurchaseOrderWriter.cs`** - **DOES NOT EXIST YET**

### **Registration in Program.cs (Lines 255-256)**

```csharp
builder.Services.AddScoped<SapGoodsIssueWriter>();
builder.Services.AddScoped<SapGoodsReceiptWriter>();
builder.Services.AddScoped<SapSalesOrderCreator>();  // Exists but for Odoo
// ❌ No SapPurchaseOrderWriter registration
```

---

## 📦 **Cache Entities**

### **Replenishment Request Structure**

**File:** `CacheLiquiMolyReplenishmentRequest` (database entity)

**Key Fields:**
```csharp
public class CacheLiquiMolyReplenishmentRequest
{
    public string RequestRef { get; set; }           // "REPL-0001"
    public string SourceProfile { get; set; }        // "LIQUIMOLY" or "MolasLubes"
    public string TargetProfile { get; set; }        // "MOLASLUBES" or "AutoHub"
    public string SourceWarehouse { get; set; }      // "WH01"
    public string TargetWarehouse { get; set; }      // "WH02"
    
    // ── Execution Results ──
    public string? TransferRef { get; set; }         // "TRFF-0001"
    public int? GoodsIssueDocEntry { get; set; }     // SAP DocEntry
    public int? GoodsIssueDocNum { get; set; }       // SAP DocNum
    public int? GoodsReceiptDocEntry { get; set; }
    public int? GoodsReceiptDocNum { get; set; }
    
    // ── NEW FIELDS NEEDED ──
    // public int? SalesOrderDocEntry { get; set; }
    // public int? SalesOrderDocNum { get; set; }
    // public int? PurchaseOrderDocEntry { get; set; }
    // public int? PurchaseOrderDocNum { get; set; }
}

public class CacheLiquiMolyReplenishmentRequestLine
{
    public string SourceItemCode { get; set; }       // "3682" (article)
    public string TargetItemCode { get; set; }       // "LUB100001"
    public string ArticleNumber { get; set; }        // "3682"
    public decimal SuggestedQty { get; set; }
    public decimal? ApprovedQty { get; set; }
    public string ExecutionStatus { get; set; }      // "PENDING", "EXECUTED", "FAILED"
}
```

---

## 🔄 **Current Transfer Service Architecture**

### **LiquiMolyTransferService.cs** (Lines 78-220)

**Current Flow:**
```csharp
public async Task<LiquiMolyTransferApplyResult> ApplyAsync(
    CreateLiquiMolyTransferRequest request,
    CancellationToken ct = default)
{
    // 1. Preflight validation
    var (error, rows) = Preflight(request);
    
    // 2. Save audit header
    var transferRef = _refGen.Generate();
    var header = new CacheLiquiMolyTransfer { ... };
    _db.CacheLiquiMolyTransfers.Add(header);
    
    // 3. Goods Issue (source DB)
    var giRef = _giWriter.CreateGoodsIssue(
        profileKey: request.SourceProfile,  // "LIQUIMOLY"
        warehouseCode: request.SourceWarehouse,
        lines: giLines
    );
    
    // 4. Goods Receipt (target DB)
    var grRef = _grWriter.CreateGoodsReceipt(
        profileKey: request.TargetProfile,  // "AutoHub"
        warehouseCode: request.TargetWarehouse,
        lines: grLines
    );
    
    return new LiquiMolyTransferApplyResult { ... };
}
```

### **Proposed Replacement Flow**

```csharp
public async Task<LiquiMolyTransferApplyResult> ApplyWithSalesAndPurchaseAsync(
    CreateLiquiMolyTransferRequest request,
    CancellationToken ct = default)
{
    // 1. Preflight validation
    var (error, rows) = Preflight(request);
    
    // 2. Save audit header
    var transferRef = _refGen.Generate();
    
    // 3. Sales Order (MolasLubes → selling to AutoHub)
    var soRef = _soWriter.CreateSalesOrder(
        profileKey: request.SourceProfile,   // "MolasLubes"
        customerCode: "C00001",              // AutoHub as customer
        priceList: 5,                        // PL05
        transferRef: transferRef,
        lines: soLines  // Uses article numbers: "3682"
    );
    
    // 4. Purchase Order (AutoHub ← buying from MolasLubes)
    var poRef = _poWriter.CreatePurchaseOrder(
        profileKey: request.TargetProfile,   // "AutoHub"
        vendorCode: "V00001",                // MolasLubes as vendor
        priceList: 5,                        // PL05 (calculated)
        transferRef: transferRef,
        salesOrderRef: $"{request.SourceProfile}:SO-{soRef.DocNum}",
        lines: poLines  // Uses LUB codes: "LUB100001"
    );
    
    // 5. Goods Receipt from PO (AutoHub inventory IN)
    var grRef = _grWriter.CreateGoodsReceiptFromPO(
        profileKey: request.TargetProfile,
        purchaseOrderDocEntry: poRef.DocEntry,
        warehouseCode: request.TargetWarehouse,
        transferRef: transferRef
    );
    
    return new LiquiMolyTransferApplyResult
    {
        SalesOrderDocEntry = soRef.DocEntry,
        SalesOrderDocNum = soRef.DocNum,
        PurchaseOrderDocEntry = poRef.DocEntry,
        PurchaseOrderDocNum = poRef.DocNum,
        GoodsReceiptDocEntry = grRef.DocEntry,
        GoodsReceiptDocNum = grRef.DocNum
    };
}
```

---

## 🎯 **Critical Questions for User**

### **1. Profile Keys**

**Current usage in code:**
- `request.SourceProfile` = ?
- `request.TargetProfile` = ?

**Options:**
- A) "MolasLubes" (source) → "AutoHub" (target)
- B) "LIQUIMOLY" (source) → "MOLASLUBES" (target)

**Action Required:** Confirm exact profile keys used in appsettings.json

---

### **2. Item Code Lookup in AutoHub**

**Question:** Does AutoHub's OITM table have:
- ❓ **U_Item_Name** UDF with article number?
- ❓ **U_ArticleNumber** custom UDF?
- ❓ Only **ItemName** contains article (no UDF)?

**SQL to check:**
```sql
USE MOLAS_Live_2021;

-- Check for UDF
SELECT TOP 1 * FROM OITM WHERE ItemCode LIKE 'LUB%';

-- Expected result example:
-- ItemCode: LUB100001
-- ItemName: 3682 TOP TEC ATF 1200 5L
-- U_Item_Name: ??? (check if exists)
-- U_ArticleNumber: ??? (check if exists)
```

---

### **3. Customer & Vendor Master Data**

**Required Setup:**

Must exist in SAP B1 BEFORE implementation:

**In MolasLubes (Molas_Lubes_LTD):**
```sql
INSERT INTO OCRD (CardCode, CardName, CardType)
VALUES ('C00001', 'AutoHub', 'C');
```

**In AutoHub (MOLAS_Live_2021):**
```sql
INSERT INTO OCRD (CardCode, CardName, CardType)
VALUES ('V00001', 'MolasLubes', 'S');  -- S = Supplier/Vendor
```

**Question:** Do these already exist? What are the exact CardCodes?

---

### **4. Price List Assignment**

**Questions:**
1. Should PL05 calculation happen:
   - ❓ At **SO creation time** (MolasLubes)?
   - ❓ At **PO creation time** (AutoHub)?
   - ❓ Both companies have separate PL05?

2. If ITM1 record missing for PL=5:
   - ❓ Calculate on-the-fly?
   - ❓ Backfill ITM1 first?
   - ❓ Use different price list?

3. Price source:
   - ❓ Read from **MolasLubes ITM1** (source)?
   - ❓ Read from **AutoHub ITM1** (target)?
   - ❓ Different prices in each company?

---

### **5. Warehouse Codes**

**Current usage:**
- `request.SourceWarehouse` = ? (MolasLubes)
- `request.TargetWarehouse` = ? (AutoHub)

**Example values needed:**
- MolasLubes: "WH01", "MAIN", "LM01"?
- AutoHub: "WH02", "AH01", "STORE"?

---

### **6. Document Linking Strategy**

**Options for cross-company reference:**

**Option A: UDF on both documents**
```
MolasLubes SO:
  U_TransferRef = "TRFF-0001"
  U_TargetPO = "AutoHub:PO-123"

AutoHub PO:
  U_TransferRef = "TRFF-0001"
  U_SourceSO = "MolasLubes:SO-456"
```

**Option B: Only in PO (simpler)**
```
AutoHub PO:
  U_TransferRef = "TRFF-0001"
  JournalMemo = "Replenishment from MolasLubes SO-456"
```

**Recommendation:** Option A for full traceability.

---

## 📋 **Next Steps**

### **Phase 1: Clarification (USER INPUT NEEDED)**

1. ✅ Confirm profile keys in `appsettings.json`
2. ✅ Verify AutoHub item lookup strategy (UDF vs ItemName)
3. ✅ Confirm Customer/Vendor CardCodes (or create them)
4. ✅ Decide pricing calculation approach
5. ✅ Confirm warehouse codes
6. ✅ Approve document linking strategy

### **Phase 2: Database Schema**

1. Add migration for new fields:
   - `CacheLiquiMolyReplenishmentRequest.SalesOrderDocEntry`
   - `CacheLiquiMolyReplenishmentRequest.SalesOrderDocNum`
   - `CacheLiquiMolyReplenishmentRequest.PurchaseOrderDocEntry`
   - `CacheLiquiMolyReplenishmentRequest.PurchaseOrderDocNum`

### **Phase 3: Service Implementation**

1. Create `SapInterCompanySalesOrderWriter`
2. Create `SapPurchaseOrderWriter`
3. Enhance `SapGoodsReceiptWriter` to support base document
4. Create `PL05PricingCalculator` service
5. Update `SapLiquiMolyItemMapper` if needed

### **Phase 4: Integration**

1. Modify `LiquiMolyReplenishmentExecutionService`
2. Update DTOs and result objects
3. Add unit tests
4. Test in dev environment

---

## 📊 **Risk Assessment**

| Risk | Impact | Mitigation |
|------|--------|------------|
| Item mapping breaks | 🔴 HIGH | Test mapper with real data before deploy |
| Price calculation wrong | 🟡 MEDIUM | Validate formula with business team |
| Customer/Vendor not set up | 🔴 HIGH | Pre-flight check in code, fail early |
| SAP DI API permission issues | 🟡 MEDIUM | Test with same user credentials |
| Transaction rollback complexity | 🟡 MEDIUM | Implement compensating actions |
| Profile configuration mismatch | 🔴 HIGH | Validate config at startup |

---

## ✅ **Ready for Design Discussion**

All existing infrastructure explored. Awaiting user input on:
1. Profile keys
2. Item lookup method
3. Customer/Vendor codes
4. Pricing strategy
5. Warehouse codes
6. Document linking approach

**Once confirmed, we can proceed with detailed technical design and implementation plan.**
