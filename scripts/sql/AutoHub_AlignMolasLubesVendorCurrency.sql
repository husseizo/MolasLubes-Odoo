-- ========================================================================
-- AutoHub - Align MolasLubes Vendor Currency to TZS
-- ========================================================================
-- Purpose: Fix existing vendor SUP00001 so inter-company purchase orders use
--          the same currency expected by the application (TZS only).
-- Usage: Execute this in MOLAS_Live_2021 before retrying PO creation.
-- ========================================================================

USE MOLAS_Live_2021;
GO

PRINT 'Checking vendor SUP00001 currency...';
PRINT '';

IF NOT EXISTS (SELECT 1 FROM OCRD WHERE CardCode = 'SUP00001')
BEGIN
    PRINT 'Vendor SUP00001 does not exist.';
    PRINT 'Run scripts/sql/AutoHub_CreateVendor_FINAL_v2.sql first.';
    RETURN;
END

SELECT
    CardCode,
    CardName,
    Currency,
    ValidFor,
    FrozenFor
FROM OCRD
WHERE CardCode = 'SUP00001';

PRINT '';
PRINT 'Updating vendor currency to TZS...';

UPDATE OCRD
SET Currency = 'TZS'
WHERE CardCode = 'SUP00001'
  AND ISNULL(Currency, '') <> 'TZS';

PRINT '';
PRINT 'Verification:';

SELECT
    CardCode,
    CardName,
    Currency,
    ValidFor,
    FrozenFor,
    UpdateDate
FROM OCRD
WHERE CardCode = 'SUP00001';

IF EXISTS (SELECT 1 FROM OCRD WHERE CardCode = 'SUP00001' AND Currency = 'TZS')
BEGIN
    PRINT '';
    PRINT 'Vendor SUP00001 is aligned to TZS.';
END
ELSE
BEGIN
    PRINT '';
    PRINT 'Vendor SUP00001 is still not aligned to TZS. Review BP master data in SAP.';
END
GO
