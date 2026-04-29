-- ========================================================================
-- Quick Vendor Verification
-- ========================================================================
-- Check if SUP00001 was actually created despite the error
-- ========================================================================

USE MOLAS_Live_2021;
GO

PRINT '🔍 Checking for vendor SUP00001...';
PRINT '';

IF EXISTS (SELECT 1 FROM OCRD WHERE CardCode = 'SUP00001')
BEGIN
    PRINT '✅ Vendor SUP00001 EXISTS';
    PRINT '';
    PRINT '📋 Vendor Details:';
    PRINT '═══════════════════════════════════════════════════════════';
    
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
    PRINT '✅ Vendor is ready for use in Purchase Orders';
END
ELSE
BEGIN
    PRINT '❌ Vendor SUP00001 DOES NOT EXIST';
    PRINT '   The INSERT failed due to truncation error.';
    PRINT '   Please run the FIXED script: AutoHub_CreateMolasLubesVendor.sql';
END

PRINT '';
GO
