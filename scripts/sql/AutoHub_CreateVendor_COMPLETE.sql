-- ========================================================================
-- AutoHub - Complete Vendor Creation Script (All-in-One)
-- ========================================================================
-- Purpose: Create vendor SUP00001 "Molas Lubes Ltd" in MOLAS_Live_2021
-- Features: 
--   - Pre-flight checks (vendor exists, field lengths)
--   - DocEntry identity handling
--   - Error handling with detailed messages
--   - Verification queries
-- 
-- Usage: Execute this entire script in MOLAS_Live_2021 database
-- ========================================================================

USE MOLAS_Live_2021;
GO

SET NOCOUNT ON;

PRINT '═══════════════════════════════════════════════════════════════';
PRINT '  AutoHub Vendor Creation - SUP00001 "Molas Lubes Ltd"';
PRINT '═══════════════════════════════════════════════════════════════';
PRINT '';

-- ========================================================================
-- STEP 1: Pre-flight Check - Does vendor already exist?
-- ========================================================================
PRINT '🔍 STEP 1: Checking if vendor already exists...';

IF EXISTS (SELECT 1 FROM OCRD WHERE CardCode = 'SUP00001')
BEGIN
    PRINT '⚠️  Vendor SUP00001 ALREADY EXISTS!';
    PRINT '';
    PRINT '📋 Current Vendor Details:';
    PRINT '───────────────────────────────────────────────────────────────';
    
    SELECT 
        DocEntry,
        CardCode,
        CardName,
        CardType,
        Currency,
        ValidFor,
        FrozenFor,
        CreateDate
    FROM OCRD 
    WHERE CardCode = 'SUP00001';
    
    PRINT '';
    PRINT '✅ No action needed - vendor is ready for use.';
    PRINT '';
    PRINT '═══════════════════════════════════════════════════════════════';
    
    -- Exit script since vendor already exists
    RETURN;
END

PRINT '✅ Vendor does not exist - proceeding with creation...';
PRINT '';

-- ========================================================================
-- STEP 2: Check U_Customer_Type field (if it exists)
-- ========================================================================
PRINT '🔍 STEP 2: Checking U_Customer_Type field...';

DECLARE @HasCustomerTypeField BIT = 0;
DECLARE @CustomerTypeMaxLength INT = 0;
DECLARE @CustomerTypeValue NVARCHAR(50) = NULL;

IF EXISTS (
    SELECT 1 
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'OCRD' 
      AND COLUMN_NAME = 'U_Customer_Type'
)
BEGIN
    SET @HasCustomerTypeField = 1;
    
    SELECT @CustomerTypeMaxLength = CHARACTER_MAXIMUM_LENGTH
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'OCRD' 
      AND COLUMN_NAME = 'U_Customer_Type';
    
    PRINT '✅ U_Customer_Type field exists';
    PRINT '   Max Length: ' + CAST(@CustomerTypeMaxLength AS VARCHAR) + ' characters';
    
    -- Determine what value to use based on field length
    IF @CustomerTypeMaxLength >= 11
    BEGIN
        SET @CustomerTypeValue = 'Other Shops';
        PRINT '   Using value: "Other Shops" (11 chars)';
    END
    ELSE IF @CustomerTypeMaxLength >= 9
    BEGIN
        SET @CustomerTypeValue = 're-seller';
        PRINT '   Using value: "re-seller" (9 chars)';
    END
    ELSE IF @CustomerTypeMaxLength >= 8
    BEGIN
        SET @CustomerTypeValue = 'reseller';
        PRINT '   Using value: "reseller" (8 chars)';
    END
    ELSE
    BEGIN
        SET @CustomerTypeValue = NULL;
        PRINT '   ⚠️  Field too short (' + CAST(@CustomerTypeMaxLength AS VARCHAR) + ' chars) - will leave empty';
    END
END
ELSE
BEGIN
    PRINT '   U_Customer_Type field does NOT exist - skipping';
END

PRINT '';

-- ========================================================================
-- STEP 3: Get next DocEntry
-- ========================================================================
PRINT '🔍 STEP 3: Calculating next DocEntry...';

DECLARE @NextDocEntry INT;
SELECT @NextDocEntry = ISNULL(MAX(DocEntry), 0) + 1 FROM OCRD;

PRINT '   Next available DocEntry: ' + CAST(@NextDocEntry AS VARCHAR);
PRINT '';

-- ========================================================================
-- STEP 4: Create Vendor
-- ========================================================================
PRINT '🚀 STEP 4: Creating vendor SUP00001...';
PRINT '';

BEGIN TRY
    -- Enable identity insert
    SET IDENTITY_INSERT OCRD ON;
    
    -- Insert with minimal required fields
    IF @HasCustomerTypeField = 1 AND @CustomerTypeValue IS NOT NULL
    BEGIN
        -- Insert WITH U_Customer_Type
        INSERT INTO OCRD (
            DocEntry,
            CardCode,
            CardName,
            CardType,
            Currency,
            ValidFor,
            FrozenFor,
            U_Customer_Type
        )
        VALUES (
            @NextDocEntry,              -- DocEntry (identity)
            'SUP00001',                 -- CardCode
            'Molas Lubes Ltd',          -- CardName
            'S',                        -- CardType (Supplier)
            'ILS',                      -- Currency
            'Y',                        -- ValidFor (Active)
            'N',                        -- FrozenFor (Not frozen)
            @CustomerTypeValue          -- U_Customer_Type
        );
        
        PRINT '✅ Vendor created successfully WITH U_Customer_Type = "' + @CustomerTypeValue + '"';
    END
    ELSE
    BEGIN
        -- Insert WITHOUT U_Customer_Type
        INSERT INTO OCRD (
            DocEntry,
            CardCode,
            CardName,
            CardType,
            Currency,
            ValidFor,
            FrozenFor
        )
        VALUES (
            @NextDocEntry,              -- DocEntry (identity)
            'SUP00001',                 -- CardCode
            'Molas Lubes Ltd',          -- CardName
            'S',                        -- CardType (Supplier)
            'ILS',                      -- Currency
            'Y',                        -- ValidFor (Active)
            'N'                         -- FrozenFor (Not frozen)
        );
        
        PRINT '✅ Vendor created successfully WITHOUT U_Customer_Type';
    END
    
    -- Disable identity insert
    SET IDENTITY_INSERT OCRD OFF;
    
    PRINT '   DocEntry assigned: ' + CAST(@NextDocEntry AS VARCHAR);
    PRINT '';
    
END TRY
BEGIN CATCH
    -- Disable identity insert in case of error
    IF (SELECT OBJECTPROPERTY(OBJECT_ID('OCRD'), 'TableHasIdentity')) = 1
    BEGIN
        SET IDENTITY_INSERT OCRD OFF;
    END
    
    PRINT '';
    PRINT '❌ ERROR CREATING VENDOR:';
    PRINT '───────────────────────────────────────────────────────────────';
    PRINT '   Error Number: ' + CAST(ERROR_NUMBER() AS VARCHAR);
    PRINT '   Error Severity: ' + CAST(ERROR_SEVERITY() AS VARCHAR);
    PRINT '   Error State: ' + CAST(ERROR_STATE() AS VARCHAR);
    PRINT '   Error Line: ' + CAST(ERROR_LINE() AS VARCHAR);
    PRINT '   Error Message: ' + ERROR_MESSAGE();
    PRINT '';
    PRINT '💡 Possible solutions:';
    PRINT '   1. Check if you have INSERT permissions on OCRD table';
    PRINT '   2. Verify database user has required privileges';
    PRINT '   3. Check if there are triggers or constraints on OCRD';
    PRINT '   4. Review the error message above for specific issue';
    PRINT '';
    PRINT '═══════════════════════════════════════════════════════════════';
    
    -- Exit script on error
    RETURN;
END CATCH

-- ========================================================================
-- STEP 5: Verification
-- ========================================================================
PRINT '🔍 STEP 5: Verifying vendor creation...';
PRINT '';

IF EXISTS (SELECT 1 FROM OCRD WHERE CardCode = 'SUP00001')
BEGIN
    PRINT '✅ VERIFICATION SUCCESSFUL!';
    PRINT '';
    PRINT '📋 Vendor Details:';
    PRINT '───────────────────────────────────────────────────────────────';
    
    SELECT 
        DocEntry        AS 'Doc Entry',
        CardCode        AS 'Vendor Code',
        CardName        AS 'Vendor Name',
        CardType        AS 'Type (S=Supplier)',
        Currency        AS 'Currency',
        ValidFor        AS 'Active (Y/N)',
        FrozenFor       AS 'Frozen (Y/N)',
        CASE 
            WHEN @HasCustomerTypeField = 1 THEN 
                ISNULL((SELECT U_Customer_Type FROM OCRD WHERE CardCode = 'SUP00001'), 'NULL')
            ELSE 'Field does not exist'
        END AS 'Customer Type',
        CreateDate      AS 'Created Date'
    FROM OCRD 
    WHERE CardCode = 'SUP00001';
    
    PRINT '';
    PRINT '═══════════════════════════════════════════════════════════════';
    PRINT '  ✅ VENDOR CREATION COMPLETE';
    PRINT '═══════════════════════════════════════════════════════════════';
    PRINT '';
    PRINT '📝 Next Steps:';
    PRINT '   1. Vendor SUP00001 is ready for use in Purchase Orders';
    PRINT '   2. You can now proceed with C# implementation';
    PRINT '   3. Run migration: dotnet ef database update';
    PRINT '   4. Implement SapPurchaseOrderWriter service';
    PRINT '';
    PRINT '📚 Documentation:';
    PRINT '   - Implementation Plan: LIQUIMOLY_INTERCOMPANY_IMPLEMENTATION_PLAN.md';
    PRINT '   - Discovery Document: LIQUIMOLY_INTERCOMPANY_DISCOVERY.md';
    PRINT '';
END
ELSE
BEGIN
    PRINT '❌ VERIFICATION FAILED!';
    PRINT '   Vendor SUP00001 was not found after creation attempt.';
    PRINT '   Please review the error messages above.';
    PRINT '';
END

PRINT '═══════════════════════════════════════════════════════════════';

SET NOCOUNT OFF;
GO
