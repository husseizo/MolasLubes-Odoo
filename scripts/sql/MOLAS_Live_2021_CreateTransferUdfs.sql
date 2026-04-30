-- Checks and creates the three transfer-tracking UDFs on OIGE and OIGN
-- in MOLAS_Live_2021 (AutoHub company).
-- Run against: MOLAS_Live_2021
--
-- These UDFs MUST exist before SapGoodsReceiptWriter can post a GR there.
-- Their absence causes a native crash (0xC0000409) in the SAP DI API.

-- ── Verify current state ──────────────────────────────────────────────────────
SELECT TableID, AliasID, Descr, TypeID, EditSize
FROM   UFD1
WHERE  TableID IN ('OIGE', 'OIGN')
  AND  AliasID IN ('TransferRef', 'FromDb', 'ToDb')
ORDER  BY TableID, AliasID;

-- ── OIGE (Goods Issue) UDFs ───────────────────────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM UFD1 WHERE TableID = 'OIGE' AND AliasID = 'TransferRef')
    INSERT INTO UFD1 (TableID, FieldID, AliasID, Descr, FieldDB, TypeID, EditSize, [Valid], ValidMsg, [Mandatory], [LinkedTable], [LinkedSys])
    SELECT 'OIGE', COALESCE(MAX(FieldID), 0) + 1, 'TransferRef', 'Transfer Ref', 'OIGE', 'db_Alpha', 50, 'N', '', 'N', '', 'N'
    FROM UFD1 WHERE TableID = 'OIGE';

IF NOT EXISTS (SELECT 1 FROM UFD1 WHERE TableID = 'OIGE' AND AliasID = 'FromDb')
    INSERT INTO UFD1 (TableID, FieldID, AliasID, Descr, FieldDB, TypeID, EditSize, [Valid], ValidMsg, [Mandatory], [LinkedTable], [LinkedSys])
    SELECT 'OIGE', COALESCE(MAX(FieldID), 0) + 1, 'FromDb', 'From Company DB', 'OIGE', 'db_Alpha', 50, 'N', '', 'N', '', 'N'
    FROM UFD1 WHERE TableID = 'OIGE';

IF NOT EXISTS (SELECT 1 FROM UFD1 WHERE TableID = 'OIGE' AND AliasID = 'ToDb')
    INSERT INTO UFD1 (TableID, FieldID, AliasID, Descr, FieldDB, TypeID, EditSize, [Valid], ValidMsg, [Mandatory], [LinkedTable], [LinkedSys])
    SELECT 'OIGE', COALESCE(MAX(FieldID), 0) + 1, 'ToDb', 'To Company DB', 'OIGE', 'db_Alpha', 50, 'N', '', 'N', '', 'N'
    FROM UFD1 WHERE TableID = 'OIGE';

-- ── OIGN (Goods Receipt) UDFs ─────────────────────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM UFD1 WHERE TableID = 'OIGN' AND AliasID = 'TransferRef')
    INSERT INTO UFD1 (TableID, FieldID, AliasID, Descr, FieldDB, TypeID, EditSize, [Valid], ValidMsg, [Mandatory], [LinkedTable], [LinkedSys])
    SELECT 'OIGN', COALESCE(MAX(FieldID), 0) + 1, 'TransferRef', 'Transfer Ref', 'OIGN', 'db_Alpha', 50, 'N', '', 'N', '', 'N'
    FROM UFD1 WHERE TableID = 'OIGN';

IF NOT EXISTS (SELECT 1 FROM UFD1 WHERE TableID = 'OIGN' AND AliasID = 'FromDb')
    INSERT INTO UFD1 (TableID, FieldID, AliasID, Descr, FieldDB, TypeID, EditSize, [Valid], ValidMsg, [Mandatory], [LinkedTable], [LinkedSys])
    SELECT 'OIGN', COALESCE(MAX(FieldID), 0) + 1, 'FromDb', 'From Company DB', 'OIGN', 'db_Alpha', 50, 'N', '', 'N', '', 'N'
    FROM UFD1 WHERE TableID = 'OIGN';

IF NOT EXISTS (SELECT 1 FROM UFD1 WHERE TableID = 'OIGN' AND AliasID = 'ToDb')
    INSERT INTO UFD1 (TableID, FieldID, AliasID, Descr, FieldDB, TypeID, EditSize, [Valid], ValidMsg, [Mandatory], [LinkedTable], [LinkedSys])
    SELECT 'OIGN', COALESCE(MAX(FieldID), 0) + 1, 'ToDb', 'To Company DB', 'OIGN', 'db_Alpha', 50, 'N', '', 'N', '', 'N'
    FROM UFD1 WHERE TableID = 'OIGN';

-- ── Confirm ───────────────────────────────────────────────────────────────────
SELECT TableID, AliasID, Descr, TypeID, EditSize
FROM   UFD1
WHERE  TableID IN ('OIGE', 'OIGN')
  AND  AliasID IN ('TransferRef', 'FromDb', 'ToDb')
ORDER  BY TableID, AliasID;
