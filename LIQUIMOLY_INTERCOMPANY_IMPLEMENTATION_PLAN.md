# LiquiMoly Inter-Company Sales & Purchase Order - Implementation Design

## Executive Summary

Replace inventory transfers (GI→GR) with inter-company sales transactions (SO→PO→GR).

**Companies:**
- MolasLubes (supplier) → Creates Sales Orders
- AutoHub (receiver) → Creates Purchase Orders + Goods Receipts

**Critical Prerequisites:**
1. ✅ Migration created
2. ⚠️ MUST CREATE VENDOR: Run `scripts/sql/AutoHub_CreateMolasLubesVendor.sql`
3. ⏳ Apply migration: `dotnet ef database update`

---

## Architecture Overview

### Current Flow (TRANSFER mode)
```
LiquiMolyReplenishmentExecutionService
  → LiquiMolyTransferService.ApplyAsync()
    → SapGoodsIssueWriter (MolasLubes MAINWHSE)
    → SapGoodsReceiptWriter (AutoHub 001/002/004)
```

### New Flow (SALES_PURCHASE mode)
```
LiquiMolyReplenishmentExecutionService
  → ApplyWithSalesAndPurchaseAsync()
    → Phase 1: Calculate PL05 prices
    → Phase 2: Create Sales Order (MolasLubes, customer=SHP00118)
    → Phase 3: Create Purchase Order (AutoHub, vendor=SUP00001)
    → Phase 4: Create Goods Receipt from PO (AutoHub)
```

---

## Implementation Components

### 1. Database Schema ✅ COMPLETED
- Migration: `20260308000000_AddSalesOrderPurchaseOrderFields.cs`
- Adds: SalesOrderDocEntry/DocNum, PurchaseOrderDocEntry/DocNum, ExecutionMode
- Status: Ready to apply

### 2. Entity Model ⏳ TODO
- File: `CacheLiquiMolyReplenishmentRequest.cs`
- Add SO/PO properties matching migration

### 3. PL05PricingCalculator ⏳ HIGH PRIORITY
- Reads ITM1 PL05, fallback to formula: `PL05 = PL01 + ((PL01 - PL03) / 2)`
- Uses existing `SapPricingReader`

### 4. SapInterCompanySalesOrderWriter ⏳ HIGH PRIORITY
- Creates ORDR in MolasLubes
- Customer: SHP00118 "Molas Autohub Solution"
- Sets U_TransferRef UDF

### 5. SapPurchaseOrderWriter ⏳ HIGH PRIORITY
- Creates OPOR in AutoHub
- Vendor: SUP00001 "Molas Lubes Ltd"
- Sets U_SourceSO, U_TransferRef UDFs

### 6. Enhanced SapGoodsReceiptWriter ⏳ MEDIUM
- Add overload for PO-based receipts
- Sets BaseType=22, BaseEntry=PO DocEntry

### 7. Execution Service Update ⏳ MEDIUM
- Add `ExecuteWithSalesAndPurchaseAsync()` method
- Orchestrates 3-phase SO→PO→GR flow

### 8. Result DTO Updates ⏳ LOW
- Add SO/PO properties to `LiquiMolyTransferApplyResult`

### 9. Service Registration ⏳ LOW
- Register new services in Program.cs

---

## Configuration

### MolasLubes (Supplier)
- Profile: "MolasLubes"
- Customer: SHP00118 ✅ EXISTS
- Warehouse: MAINWHSE
- Items: Article codes (e.g., "3682")
- Price List: PL05

### AutoHub (Receiver)
- Profile: "AutoHub"
- Vendor: SUP00001 ⚠️ MUST CREATE
- Warehouses: 001, 002, 004
- Items: LUB codes (e.g., "LUB100001")
- Item Name UDF: U_Item_Name ✅ CONFIRMED

### Item Mapping
```
MolasLubes "3682" → AutoHub "LUB100001"
Search by: U_Item_Name contains article number
```

---

## Critical Prerequisites

### 1. CREATE VENDOR (BLOCKER) ✅ FIXED

**Critical Discovery:** SAP B1 OCRD.DocEntry is NOT an identity column!
- SAP manages DocEntry via triggers/stored procedures
- DO NOT use `SET IDENTITY_INSERT`
- Calculate next DocEntry and insert directly

**SOLUTION - Use This Script:**

```bash
# FINAL WORKING VERSION (no IDENTITY_INSERT)
scripts/sql/AutoHub_CreateVendor_FINAL.sql
```

**What It Does:**
1. ✅ Checks if vendor exists (idempotent)
2. ✅ Detects U_Customer_Type field length
3. ✅ Calculates next DocEntry (MAX + 1)
4. ✅ Inserts directly WITHOUT IDENTITY_INSERT
5. ✅ Handles field truncation ("re-seller" for 10 char max)
6. ✅ Full error handling with detailed messages
7. ✅ Verification query

**Common Errors FIXED:**
- ❌ "String or binary data truncated" → Script auto-detects max length
  - Max 10 chars → Uses "re-seller" (9 chars) ✅
- ❌ "Cannot insert NULL into DocEntry" → Script calculates DocEntry ✅
- ❌ "Table does not have identity property" → Removed IDENTITY_INSERT ✅

### 2. Apply Migration
```bash
cd src/MolasLubes.Api
dotnet ef database update
```

### 3. Verify SAP UDFs
- ORDR: U_TransferRef
- OPOR: U_TransferRef, U_SourceSO

---

## Implementation Order

### Sprint 1: Foundation (Days 1-2)
1. ✅ Discovery document
2. ✅ Migration
3. ⚠️ User: Run vendor SQL
4. Apply migration
5. Update entity model

### Sprint 2: Core Services (Days 3-5)
6. PL05PricingCalculator
7. SapInterCompanySalesOrderWriter
8. Unit tests

### Sprint 3: Purchase Flow (Days 6-8)
9. SapPurchaseOrderWriter
10. Enhanced SapGoodsReceiptWriter
11. Unit tests

### Sprint 4: Orchestration (Days 9-10)
12. Update execution service
13. Update DTOs
14. Register services

### Sprint 5: Testing (Days 11-14)
15. Integration tests
16. UAT

### Sprint 6: Deployment (Day 15)
17. Deploy to production

---

## Quick Start for User

**IMMEDIATE ACTION REQUIRED:**

1. Open SQL Server Management Studio
2. Connect to SAP SQL Server
3. Open: `scripts/sql/AutoHub_CreateMolasLubesVendor.sql`
4. Execute against `MOLAS_Live_2021`
5. Verify:
   ```sql
   SELECT CardCode, CardName FROM OCRD WHERE CardCode = 'SUP00001';
   ```
6. Reply: "Vendor SUP00001 created ✅"

---

## Reference Documents
- Discovery: `LIQUIMOLY_INTERCOMPANY_DISCOVERY.md`
- Migration: `20260308000000_AddSalesOrderPurchaseOrderFields.cs`
- Vendor SQL: `scripts/sql/AutoHub_CreateMolasLubesVendor.sql`

---

**STATUS:** ⚠️ Awaiting vendor creation. Once SUP00001 exists, implementation begins immediately.
