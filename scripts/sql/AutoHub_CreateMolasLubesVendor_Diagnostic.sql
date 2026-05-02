-- ========================================================================
-- Diagnostic Script - Find Column Lengths for OCRD
-- ========================================================================
-- Run this first to identify which field is causing truncation
-- ========================================================================

USE MOLAS_Live_2021;
GO

PRINT '📊 OCRD Table Column Definitions:';
PRINT '═══════════════════════════════════════════════════════════════';

SELECT 
    COLUMN_NAME,
    DATA_TYPE,
    CHARACTER_MAXIMUM_LENGTH,
    IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'OCRD'
  AND COLUMN_NAME IN (
      'CardCode',
      'CardName',
      'CardType',
      'CardFName',
      'GroupCode',
      'Currency',
      'ValidFor',
      'FrozenFor',
      'U_Customer_Type'
  )
ORDER BY ORDINAL_POSITION;

PRINT '';
PRINT '📋 Value Lengths in Your Script:';
PRINT '─────────────────────────────────────────────────────────────';
PRINT 'CardCode: SUP00001 (8 chars)';
PRINT 'CardName: Molas Lubes Ltd (15 chars)';
PRINT 'CardType: S (1 char)';
PRINT 'CardFName: Molas Lubes Ltd (15 chars)';
PRINT 'GroupCode: 100 (numeric)';
PRINT 'Currency: TZS (3 chars)';
PRINT 'ValidFor: Y (1 char)';
PRINT 'FrozenFor: N (1 char)';
PRINT 'U_Customer_Type: Other Shops (11 chars)';
PRINT '';

-- Check if U_Customer_Type field exists
IF NOT EXISTS (
    SELECT 1 
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'OCRD' 
      AND COLUMN_NAME = 'U_Customer_Type'
)
BEGIN
    PRINT '⚠️  WARNING: U_Customer_Type does not exist in OCRD table!';
    PRINT '   This might be the cause of the error.';
END
ELSE
BEGIN
    PRINT '✅ U_Customer_Type field exists';
END

PRINT '';
PRINT '🔍 Check Existing Vendor Groups:';
PRINT '─────────────────────────────────────────────────────────────';
SELECT TOP 5 GroupCode, GroupName 
FROM OCRG 
WHERE GroupType = 'S'  -- Supplier groups
ORDER BY GroupCode;

PRINT '';
GO
