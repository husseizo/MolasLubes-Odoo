-- ========================================================================
-- Quick Vendor Verification
-- ========================================================================
-- Checks whether SUP00001 exists and whether it is aligned to TZS.
-- ========================================================================

USE MOLAS_Live_2021;
GO

PRINT 'Checking for vendor SUP00001...';
PRINT '';

IF EXISTS (SELECT 1 FROM OCRD WHERE CardCode = 'SUP00001')
BEGIN
    PRINT 'Vendor SUP00001 EXISTS';
    PRINT '';
    PRINT 'Vendor Details:';

    SELECT
        CardCode,
        CardName,
        CardType,
        Currency,
        ValidFor,
        GroupCode,
        CreateDate
    FROM OCRD
    WHERE CardCode = 'SUP00001';

    PRINT '';

    IF EXISTS (SELECT 1 FROM OCRD WHERE CardCode = 'SUP00001' AND Currency = 'TZS')
    BEGIN
        PRINT 'Vendor is ready for use in Purchase Orders (Currency = TZS)';
    END
    ELSE
    BEGIN
        PRINT 'Vendor currency is NOT TZS';
        PRINT 'Run scripts/sql/AutoHub_AlignMolasLubesVendorCurrency.sql before retrying PO creation.';
    END
END
ELSE
BEGIN
    PRINT 'Vendor SUP00001 DOES NOT EXIST';
    PRINT 'Run scripts/sql/AutoHub_CreateVendor_FINAL_v2.sql first.';
END

PRINT '';
GO
