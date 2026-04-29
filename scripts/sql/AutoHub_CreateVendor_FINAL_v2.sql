/*
================================================================================
  SAP B1 VENDOR CREATION SCRIPT - FINAL VERSION
  
  Purpose: Create vendor SUP00001 "Molas Lubes Ltd" in AutoHub database
  Database: MOLAS_Live_2021 (AutoHub)
  Table: OCRD (Business Partners - Vendors)
  
  FIXES APPLIED:
  ✅ Error 1: U_Customer_Type string truncation (auto-detects max length)
  ✅ Error 2: DocEntry IDENTITY property error 8106 (no IDENTITY_INSERT, manual calculation)
  ✅ Error 3: U_Phone required field error 515 (detects NOT NULL, inserts "N/A")
  
  CRITICAL LEARNINGS:
  - SAP B1 OCRD.DocEntry is NOT a SQL Server IDENTITY column
  - SAP manages DocEntry via triggers/stored procedures
  - Must calculate DocEntry manually: MAX(DocEntry) + 1
  - Never use SET IDENTITY_INSERT on SAP tables
  - Must satisfy all NOT NULL constraints including UDFs
  
  Created: 2025-01-08
  Last Updated: 2025-01-08 (Added U_Phone handling)
================================================================================
*/

USE MOLAS_Live_2021;
GO

PRINT '';
PRINT '================================================================================';
PRINT '  SAP B1 VENDOR CREATION - AutoHub Database';
PRINT '  Vendor: SUP00001 - Molas Lubes Ltd';
PRINT '  Purpose: Enable inter-company purchase orders';
PRINT '================================================================================';
PRINT '';

-- ============================================================================
-- STEP 1: PRE-FLIGHT CHECK
-- ============================================================================
PRINT '';
PRINT '--- STEP 1: PRE-FLIGHT CHECK ---';
PRINT 'Checking if vendor SUP00001 already exists...';

IF EXISTS (SELECT 1 FROM OCRD WHERE CardCode = 'SUP00001')
BEGIN
    PRINT '⚠️  WARNING: Vendor SUP00001 already exists!';
    PRINT '';
    PRINT 'Existing vendor details:';
    SELECT 
        CardCode, 
        CardName, 
        CardType,
        Currency,
        U_Customer_Type,
        U_Phone,
        ValidFor,
        FrozenFor,
        CreateDate
    FROM OCRD 
    WHERE CardCode = 'SUP00001';
    
    PRINT '';
    PRINT '❌ SCRIPT ABORTED - Vendor already exists.';
    PRINT 'To update the existing vendor, use an UPDATE statement instead.';
    PRINT '';
    
    -- Exit gracefully
    RETURN;
END

PRINT '✅ Vendor does not exist. Proceeding with creation...';
PRINT '';

-- ============================================================================
-- STEP 2: DETECT U_Customer_Type FIELD CONSTRAINTS
-- ============================================================================
PRINT '';
PRINT '--- STEP 2: DETECT U_Customer_Type CONSTRAINTS ---';

DECLARE @HasCustomerTypeField BIT = 0;
DECLARE @CustomerTypeMaxLength INT = NULL;
DECLARE @CustomerTypeValue NVARCHAR(50) = NULL;

-- Check if U_Customer_Type field exists and get its max length
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
    WHERE TABLE_NAME = 'OCRD' AND COLUMN_NAME = 'U_Customer_Type';
    
    PRINT '✅ U_Customer_Type field exists in OCRD table';
    PRINT '   Max Length: ' + CAST(@CustomerTypeMaxLength AS NVARCHAR(10)) + ' characters';
    
    -- Select appropriate value based on max length
    IF @CustomerTypeMaxLength >= 11
    BEGIN
        SET @CustomerTypeValue = 'Other Shops';
        PRINT '   Selected value: "Other Shops" (11 characters)';
    END
    ELSE IF @CustomerTypeMaxLength >= 9
    BEGIN
        SET @CustomerTypeValue = 're-seller';
        PRINT '   Selected value: "re-seller" (9 characters) ✅ FITS';
    END
    ELSE IF @CustomerTypeMaxLength >= 8
    BEGIN
        SET @CustomerTypeValue = 'reseller';
        PRINT '   Selected value: "reseller" (8 characters)';
    END
    ELSE IF @CustomerTypeMaxLength >= 5
    BEGIN
        SET @CustomerTypeValue = 'other';
        PRINT '   Selected value: "other" (5 characters)';
    END
    ELSE
    BEGIN
        -- If max length is too small, don't use the field
        SET @CustomerTypeValue = NULL;
        PRINT '   ⚠️  Max length too small, will not populate this field';
    END
END
ELSE
BEGIN
    PRINT 'ℹ️  U_Customer_Type field not found in OCRD table (optional UDF)';
END

PRINT '';

-- ============================================================================
-- STEP 3: DETECT U_Phone FIELD REQUIREMENT
-- ============================================================================
PRINT '';
PRINT '--- STEP 3: DETECT U_Phone REQUIREMENT ---';

DECLARE @UPhoneRequired BIT = 0;
DECLARE @UPhoneMaxLength INT = NULL;

-- Check if U_Phone field exists and if it's required (NOT NULL)
IF EXISTS (
    SELECT 1 
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'OCRD' 
      AND COLUMN_NAME = 'U_Phone'
)
BEGIN
    SELECT 
        @UPhoneRequired = CASE WHEN IS_NULLABLE = 'NO' THEN 1 ELSE 0 END,
        @UPhoneMaxLength = CHARACTER_MAXIMUM_LENGTH
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'OCRD' AND COLUMN_NAME = 'U_Phone';
    
    PRINT '✅ U_Phone field exists in OCRD table';
    PRINT '   Max Length: ' + CAST(ISNULL(@UPhoneMaxLength, 0) AS NVARCHAR(10)) + ' characters';
    
    IF @UPhoneRequired = 1
    BEGIN
        PRINT '   ⚠️  U_Phone is REQUIRED (NOT NULL) - will use "N/A" as placeholder';
    END
    ELSE
    BEGIN
        PRINT '   ℹ️  U_Phone is optional (allows NULL) - will populate with "N/A"';
    END
END
ELSE
BEGIN
    PRINT 'ℹ️  U_Phone field not found in OCRD table (optional UDF)';
END

PRINT '';

-- ============================================================================
-- STEP 4: CALCULATE NEXT DocEntry (NO IDENTITY_INSERT!)
-- ============================================================================
PRINT '';
PRINT '--- STEP 4: CALCULATE NEXT DocEntry ---';
PRINT 'SAP B1 OCRD.DocEntry is NOT a SQL Server IDENTITY column';
PRINT 'Calculating next DocEntry manually...';

DECLARE @NextDocEntry INT;

SELECT @NextDocEntry = ISNULL(MAX(DocEntry), 0) + 1 
FROM OCRD;

PRINT 'Next available DocEntry: ' + CAST(@NextDocEntry AS NVARCHAR(10));
PRINT '';

-- ============================================================================
-- STEP 5: CREATE VENDOR (CONDITIONAL INSERT BASED ON DETECTED FIELDS)
-- ============================================================================
PRINT '';
PRINT '--- STEP 5: CREATE VENDOR ---';
PRINT 'Inserting vendor SUP00001 with detected field configuration...';

-- We now have 4 possible INSERT scenarios based on field availability:
-- 1. Has U_Customer_Type AND requires U_Phone
-- 2. Has U_Customer_Type but U_Phone optional
-- 3. No U_Customer_Type but requires U_Phone  
-- 4. Neither U_Customer_Type nor U_Phone

BEGIN TRY

    -- Scenario 1: Both U_Customer_Type and U_Phone
    IF @HasCustomerTypeField = 1 AND @CustomerTypeValue IS NOT NULL AND @UPhoneRequired = 1
    BEGIN
        PRINT 'ℹ️  Creating vendor WITH U_Customer_Type and WITH U_Phone (required)...';
        
        INSERT INTO OCRD (
            DocEntry,
            CardCode,
            CardName,
            CardType,
            Currency,
            ValidFor,
            FrozenFor,
            U_Customer_Type,
            U_Phone
        )
        VALUES (
            @NextDocEntry,          -- DocEntry (calculated manually)
            'SUP00001',             -- CardCode (Vendor ID)
            'Molas Lubes Ltd',      -- CardName (Vendor Name)
            'S',                    -- CardType (S = Supplier/Vendor)
            'ILS',                  -- Currency (Israeli Shekel)
            'Y',                    -- ValidFor (Y = Active)
            'N',                    -- FrozenFor (N = Not Frozen)
            @CustomerTypeValue,     -- U_Customer_Type (detected value)
            'N/A'                   -- U_Phone (required field, system vendor)
        );
        
        PRINT '✅ Vendor created successfully WITH U_Customer_Type = "' + @CustomerTypeValue + '" and U_Phone = "N/A"';
    END
    
    -- Scenario 2: U_Customer_Type only (U_Phone optional)
    ELSE IF @HasCustomerTypeField = 1 AND @CustomerTypeValue IS NOT NULL AND @UPhoneRequired = 0
    BEGIN
        PRINT 'ℹ️  Creating vendor WITH U_Customer_Type and WITHOUT U_Phone (optional)...';
        
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
            @NextDocEntry,          -- DocEntry (calculated manually)
            'SUP00001',             -- CardCode (Vendor ID)
            'Molas Lubes Ltd',      -- CardName (Vendor Name)
            'S',                    -- CardType (S = Supplier/Vendor)
            'ILS',                  -- Currency (Israeli Shekel)
            'Y',                    -- ValidFor (Y = Active)
            'N',                    -- FrozenFor (N = Not Frozen)
            @CustomerTypeValue      -- U_Customer_Type (detected value)
        );
        
        PRINT '✅ Vendor created successfully WITH U_Customer_Type = "' + @CustomerTypeValue + '"';
    END
    
    -- Scenario 3: U_Phone required but no U_Customer_Type
    ELSE IF (@HasCustomerTypeField = 0 OR @CustomerTypeValue IS NULL) AND @UPhoneRequired = 1
    BEGIN
        PRINT 'ℹ️  Creating vendor WITHOUT U_Customer_Type but WITH U_Phone (required)...';
        
        INSERT INTO OCRD (
            DocEntry,
            CardCode,
            CardName,
            CardType,
            Currency,
            ValidFor,
            FrozenFor,
            U_Phone
        )
        VALUES (
            @NextDocEntry,          -- DocEntry (calculated manually)
            'SUP00001',             -- CardCode (Vendor ID)
            'Molas Lubes Ltd',      -- CardName (Vendor Name)
            'S',                    -- CardType (S = Supplier/Vendor)
            'ILS',                  -- Currency (Israeli Shekel)
            'Y',                    -- ValidFor (Y = Active)
            'N',                    -- FrozenFor (N = Not Frozen)
            'N/A'                   -- U_Phone (required field, system vendor)
        );
        
        PRINT '✅ Vendor created successfully WITH U_Phone = "N/A"';
    END
    
    -- Scenario 4: Neither field (minimal insert)
    ELSE
    BEGIN
        PRINT 'ℹ️  Creating vendor WITHOUT optional UDFs (minimal insert)...';
        
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
            @NextDocEntry,          -- DocEntry (calculated manually)
            'SUP00001',             -- CardCode (Vendor ID)
            'Molas Lubes Ltd',      -- CardName (Vendor Name)
            'S',                    -- CardType (S = Supplier/Vendor)
            'ILS',                  -- Currency (Israeli Shekel)
            'Y',                    -- ValidFor (Y = Active)
            'N'                     -- FrozenFor (N = Not Frozen)
        );
        
        PRINT '✅ Vendor created successfully (minimal fields only)';
    END

END TRY
BEGIN CATCH
    PRINT '';
    PRINT '❌ ERROR CREATING VENDOR:';
    PRINT '   Error Number: ' + CAST(ERROR_NUMBER() AS NVARCHAR(10));
    PRINT '   Error Message: ' + ERROR_MESSAGE();
    PRINT '   Error Line: ' + CAST(ERROR_LINE() AS NVARCHAR(10));
    PRINT '';
    PRINT '🔍 TROUBLESHOOTING TIPS:';
    PRINT '   1. Run AutoHub_DiagnoseRequiredFields.sql to find all NOT NULL columns';
    PRINT '   2. Check if there are additional required UDFs (columns starting with U_)';
    PRINT '   3. Review SAP_B1_VENDOR_CREATION_ERROR_LOG.md for known errors';
    PRINT '';
    
    THROW;
END CATCH

PRINT '';

-- ============================================================================
-- STEP 6: VERIFICATION
-- ============================================================================
PRINT '';
PRINT '--- STEP 6: VERIFICATION ---';
PRINT 'Verifying vendor creation...';
PRINT '';

IF EXISTS (SELECT 1 FROM OCRD WHERE CardCode = 'SUP00001')
BEGIN
    PRINT '✅ VERIFICATION SUCCESSFUL!';
    PRINT '';
    PRINT 'Vendor details:';
    
    SELECT 
        DocEntry,
        CardCode, 
        CardName, 
        CardType,
        Currency,
        U_Customer_Type,
        U_Phone,
        ValidFor,
        FrozenFor,
        CreateDate
    FROM OCRD 
    WHERE CardCode = 'SUP00001';
    
    PRINT '';
    PRINT '================================================================================';
    PRINT '  ✅ SUCCESS - Vendor SUP00001 created in AutoHub!';
    PRINT '================================================================================';
    PRINT '';
    PRINT '📋 NEXT STEPS:';
    PRINT '   1. Apply EF Core migration: dotnet ef database update';
    PRINT '   2. Update CacheLiquiMolyReplenishmentRequest entity (add SO/PO properties)';
    PRINT '   3. Implement PL05PricingCalculator service';
    PRINT '   4. Implement SapInterCompanySalesOrderWriter (create ORDR in MolasLubes)';
    PRINT '   5. Implement SapPurchaseOrderWriter (create OPOR in AutoHub)';
    PRINT '   6. Enhance SapGoodsReceiptWriter for PO-based receipts';
    PRINT '   7. Update LiquiMolyReplenishmentExecutionService orchestration';
    PRINT '   8. Test end-to-end SO→PO→GR flow';
    PRINT '';
    PRINT '📚 REFERENCE DOCUMENTS:';
    PRINT '   - LIQUIMOLY_INTERCOMPANY_IMPLEMENTATION_PLAN.md (complete implementation guide)';
    PRINT '   - SAP_B1_VENDOR_CREATION_ERROR_LOG.md (error troubleshooting)';
    PRINT '';
END
ELSE
BEGIN
    PRINT '❌ VERIFICATION FAILED!';
    PRINT '';
    PRINT 'Vendor SUP00001 was NOT created. Please review errors above.';
    PRINT 'Run AutoHub_DiagnoseRequiredFields.sql to identify missing required fields.';
    PRINT '';
END

PRINT '';
PRINT '================================================================================';
PRINT '';
GO
