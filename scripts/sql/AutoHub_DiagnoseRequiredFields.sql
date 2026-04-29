-- ========================================================================
-- Find ALL Required (NOT NULL) Columns in OCRD Table
-- ========================================================================
-- Purpose: Identify all columns that MUST have values when inserting
-- This will tell us which fields we're missing in our INSERT statement
-- ========================================================================

USE MOLAS_Live_2021;
GO

PRINT '🔍 Finding ALL required (NOT NULL) columns in OCRD table...';
PRINT '';
PRINT '📋 Columns that MUST have values:';
PRINT '═══════════════════════════════════════════════════════════════';

SELECT 
    COLUMN_NAME AS 'Column Name',
    DATA_TYPE AS 'Data Type',
    CHARACTER_MAXIMUM_LENGTH AS 'Max Length',
    COLUMN_DEFAULT AS 'Default Value',
    CASE 
        WHEN COLUMN_NAME LIKE 'U_%' THEN 'User-Defined Field (UDF)'
        ELSE 'Standard SAP Field'
    END AS 'Field Type'
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'OCRD'
  AND IS_NULLABLE = 'NO'                -- NOT NULL columns
  AND COLUMN_NAME NOT IN ('DocEntry')   -- Exclude DocEntry (we handle it separately)
ORDER BY 
    CASE WHEN COLUMN_NAME LIKE 'U_%' THEN 2 ELSE 1 END,  -- Standard fields first
    COLUMN_NAME;

PRINT '';
PRINT '📊 Analyzing specific UDF fields...';
PRINT '───────────────────────────────────────────────────────────────';

-- Check U_Phone specifically
IF EXISTS (
    SELECT 1 
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'OCRD' 
      AND COLUMN_NAME = 'U_Phone'
      AND IS_NULLABLE = 'NO'
)
BEGIN
    PRINT '⚠️  U_Phone is REQUIRED (NOT NULL)';
    
    SELECT 
        CHARACTER_MAXIMUM_LENGTH AS 'U_Phone Max Length',
        DATA_TYPE AS 'Data Type'
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'OCRD' 
      AND COLUMN_NAME = 'U_Phone';
    
    PRINT '   💡 Suggestion: Use empty string or placeholder like "N/A"';
END

PRINT '';
PRINT '📝 Sample values from existing vendors:';
PRINT '───────────────────────────────────────────────────────────────';

-- Show sample values for required UDF fields
SELECT TOP 5
    CardCode,
    CardName,
    CardType,
    ISNULL(U_Phone, '<NULL>') AS U_Phone,
    ISNULL(U_Customer_Type, '<NULL>') AS U_Customer_Type
FROM OCRD
WHERE CardType = 'S'  -- Suppliers only
ORDER BY CreateDate DESC;

PRINT '';
PRINT '✅ Diagnostic complete - use this information to fix INSERT statement';
PRINT '';
GO
