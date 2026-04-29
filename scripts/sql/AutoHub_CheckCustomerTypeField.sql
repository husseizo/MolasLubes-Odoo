-- ========================================================================
-- Check U_Customer_Type Field Length
-- ========================================================================
-- Run this to see how long the U_Customer_Type field is
-- This will tell us what value we can safely use
-- ========================================================================

USE MOLAS_Live_2021;
GO

PRINT '🔍 Checking U_Customer_Type field definition...';
PRINT '';

-- Check if field exists and get its length
IF EXISTS (
    SELECT 1 
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'OCRD' 
      AND COLUMN_NAME = 'U_Customer_Type'
)
BEGIN
    SELECT 
        COLUMN_NAME,
        DATA_TYPE,
        CHARACTER_MAXIMUM_LENGTH AS MaxLength,
        IS_NULLABLE
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'OCRD' 
      AND COLUMN_NAME = 'U_Customer_Type';
    
    PRINT '';
    PRINT '📊 Value length analysis:';
    PRINT '   "re-seller" = 9 chars';
    PRINT '   "reseller" = 8 chars';
    PRINT '   "Other Shops" = 11 chars';
    PRINT '';
    
    -- Check what values are currently used
    PRINT '📋 Current values in use:';
    SELECT DISTINCT 
        U_Customer_Type AS CurrentValue,
        LEN(U_Customer_Type) AS Length,
        COUNT(*) AS Count
    FROM OCRD
    WHERE U_Customer_Type IS NOT NULL
    GROUP BY U_Customer_Type
    ORDER BY COUNT(*) DESC;
END
ELSE
BEGIN
    PRINT '❌ U_Customer_Type field does NOT exist in OCRD table';
    PRINT '   You can create the vendor without this field';
END

PRINT '';
GO
