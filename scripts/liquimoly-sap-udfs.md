# SAP UDF Creation — Liqui Moly Replenishment

Create these three UDFs on **both** `OIGE` (Goods Issue) and `OIGN` (Goods Receipt)
in **both** SAP companies: `Molas_Lubes_LTD` (source) and `MOLAS_Live_2021` (target).

Do this once, before the first API call that posts a GI or GR.

---

## Via SAP B1 UI

**Tools → Customization Tools → User-Defined Fields → Manage User Fields**

Select table `Inventory Goods Issue` (OIGE) or `Inventory Goods Receipt` (OIGN), then add:

| Field Name    | Type        | Length | Title             |
|---------------|-------------|--------|-------------------|
| U_TransferRef | Alphanumeric | 50    | Transfer Ref      |
| U_FromDb      | Alphanumeric | 50    | From Company DB   |
| U_ToDb        | Alphanumeric | 50    | To Company DB     |

Repeat for the other table. Then switch companies and repeat both tables.

---

## Via SDK / SQL (advanced)

If you prefer scripting, run the following in each company database.
**Back up first — schema changes in SAP are not transactional.**

```sql
-- OIGE (Goods Issue)
INSERT INTO UFD1 (TableID, FieldID, AliasID, Descr, FieldDB, TypeID, EditSize, Valid, ValidMsg)
SELECT 'OIGE', COALESCE(MAX(FieldID),0)+1, 'TransferRef', 'Transfer Ref',     'OIGE', 'db_Alpha', 50, 'N', ''
FROM UFD1 WHERE TableID = 'OIGE' AND NOT EXISTS (SELECT 1 FROM UFD1 WHERE TableID='OIGE' AND AliasID='TransferRef');

INSERT INTO UFD1 (TableID, FieldID, AliasID, Descr, FieldDB, TypeID, EditSize, Valid, ValidMsg)
SELECT 'OIGE', COALESCE(MAX(FieldID),0)+1, 'FromDb', 'From Company DB', 'OIGE', 'db_Alpha', 50, 'N', ''
FROM UFD1 WHERE TableID = 'OIGE' AND NOT EXISTS (SELECT 1 FROM UFD1 WHERE TableID='OIGE' AND AliasID='FromDb');

INSERT INTO UFD1 (TableID, FieldID, AliasID, Descr, FieldDB, TypeID, EditSize, Valid, ValidMsg)
SELECT 'OIGE', COALESCE(MAX(FieldID),0)+1, 'ToDb',   'To Company DB',   'OIGE', 'db_Alpha', 50, 'N', ''
FROM UFD1 WHERE TableID = 'OIGE' AND NOT EXISTS (SELECT 1 FROM UFD1 WHERE TableID='OIGE' AND AliasID='ToDb');

-- OIGN (Goods Receipt) — same three fields
INSERT INTO UFD1 (TableID, FieldID, AliasID, Descr, FieldDB, TypeID, EditSize, Valid, ValidMsg)
SELECT 'OIGN', COALESCE(MAX(FieldID),0)+1, 'TransferRef', 'Transfer Ref',     'OIGN', 'db_Alpha', 50, 'N', ''
FROM UFD1 WHERE TableID = 'OIGN' AND NOT EXISTS (SELECT 1 FROM UFD1 WHERE TableID='OIGN' AND AliasID='TransferRef');

INSERT INTO UFD1 (TableID, FieldID, AliasID, Descr, FieldDB, TypeID, EditSize, Valid, ValidMsg)
SELECT 'OIGN', COALESCE(MAX(FieldID),0)+1, 'FromDb', 'From Company DB', 'OIGN', 'db_Alpha', 50, 'N', ''
FROM UFD1 WHERE TableID = 'OIGN' AND NOT EXISTS (SELECT 1 FROM UFD1 WHERE TableID='OIGN' AND AliasID='FromDb');

INSERT INTO UFD1 (TableID, FieldID, AliasID, Descr, FieldDB, TypeID, EditSize, Valid, ValidMsg)
SELECT 'OIGN', COALESCE(MAX(FieldID),0)+1, 'ToDb',   'To Company DB',   'OIGN', 'db_Alpha', 50, 'N', ''
FROM UFD1 WHERE TableID = 'OIGN' AND NOT EXISTS (SELECT 1 FROM UFD1 WHERE TableID='OIGN' AND AliasID='ToDb');
```

> **Note:** SAP B1 UDF IDs must be contiguous and unique per table.
> The `COALESCE(MAX(FieldID),0)+1` expression allocates the next available ID.
> If another add-on has already added UDFs to these tables, verify the ID range first with:
> ```sql
> SELECT TableID, MAX(FieldID) FROM UFD1 WHERE TableID IN ('OIGE','OIGN') GROUP BY TableID;
> ```

---

## Verification

After creation, confirm the columns exist in both companies:

```sql
-- Should return 3 rows per table (U_TransferRef, U_FromDb, U_ToDb)
SELECT TableID, AliasID, Descr FROM UFD1
WHERE TableID IN ('OIGE','OIGN')
  AND AliasID IN ('TransferRef','FromDb','ToDb')
ORDER BY TableID, AliasID;
```

And confirm the physical columns were added to the SAP tables:

```sql
SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME IN ('OIGE','OIGN')
  AND COLUMN_NAME IN ('U_TransferRef','U_FromDb','U_ToDb')
ORDER BY TABLE_NAME, COLUMN_NAME;
-- Expected: 6 rows
```
