-- ========================================================================
-- AutoHub - Create Vendor via Direct INSERT (WITH IDENTITY_INSERT)
-- ========================================================================
-- This is the SQL-only approach for creating vendor SUP00001
-- WARNING: This bypasses SAP DI API - use only if DI API is not available
-- ========================================================================

USE MOLAS_Live_2021;
GO

-- Step 1: Check if vendor already exists
IF EXISTS (SELECT 1 FROM OCRD WHERE CardCode = 'SUP00001')
BEGIN
    PRINT '⚠️  Vendor SUP00001 already exists. Skipping creation.';
    
    SELECT 
        CardCode,
        CardName,
        CardType,
        Currency,
        ValidFor
    FROM OCRD 
    WHERE CardCode = 'SUP00001';
END
ELSE
BEGIN
    PRINT '✅ Creating vendor SUP00001 "Molas Lubes Ltd"...';
    PRINT '';
    
    -- Step 2: Get next available DocEntry
    DECLARE @NextDocEntry INT;
    SELECT @NextDocEntry = ISNULL(MAX(DocEntry), 0) + 1 FROM OCRD;
    PRINT '📝 Using DocEntry: ' + CAST(@NextDocEntry AS VARCHAR);
    
    -- Step 3: Enable identity insert
    SET IDENTITY_INSERT OCRD ON;
    
    -- Step 4: Insert vendor with minimal required fields
    BEGIN TRY
        INSERT INTO OCRD (
            DocEntry,           -- Identity field (required)
            CardCode,           -- Vendor code
            CardName,           -- Vendor name
            CardType,           -- S = Supplier
            Currency,           -- TZS
            ValidFor,           -- Y = Active
            FrozenFor           -- N = Not frozen
        )
        VALUES (
            @NextDocEntry,      -- DocEntry (calculated above)
            'SUP00001',         -- CardCode
            'Molas Lubes Ltd',  -- CardName
            'S',                -- CardType (Supplier)
            'TZS',              -- Currency
            'Y',                -- ValidFor
            'N'                 -- FrozenFor
        );
        
        PRINT '✅ Vendor SUP00001 created successfully with DocEntry ' + CAST(@NextDocEntry AS VARCHAR);
    END TRY
    BEGIN CATCH
        PRINT '❌ Error creating vendor:';
        PRINT '   Error Number: ' + CAST(ERROR_NUMBER() AS VARCHAR);
        PRINT '   Error Message: ' + ERROR_MESSAGE();
        PRINT '   Error Line: ' + CAST(ERROR_LINE() AS VARCHAR);
    END CATCH
    
    -- Step 5: Disable identity insert
    SET IDENTITY_INSERT OCRD OFF;
    
    -- Step 6: Optionally update U_Customer_Type if field exists
    IF EXISTS (
        SELECT 1 
        FROM INFORMATION_SCHEMA.COLUMNS 
        WHERE TABLE_NAME = 'OCRD' 
          AND COLUMN_NAME = 'U_Customer_Type'
    )
    BEGIN
        BEGIN TRY
            UPDATE OCRD 
            SET U_Customer_Type = 'reseller'  -- Shortened to avoid truncation
            WHERE CardCode = 'SUP00001';
            
            PRINT '✅ U_Customer_Type updated';
        END TRY
        BEGIN CATCH
            PRINT '⚠️  Could not update U_Customer_Type (field may be too short)';
        END CATCH
    END
END
GO

-- Step 7: Final verification
PRINT '';
PRINT '📋 Final Verification:';
PRINT '═══════════════════════════════════════════════════════════════';

SELECT 
    DocEntry,
    CardCode,
    CardName,
    CardType,
    Currency,
    ValidFor,
    FrozenFor,
    CASE 
        WHEN EXISTS (
            SELECT 1 
            FROM INFORMATION_SCHEMA.COLUMNS 
            WHERE TABLE_NAME = 'OCRD' 
              AND COLUMN_NAME = 'U_Customer_Type'
        ) THEN 'UDF exists'
        ELSE 'UDF does not exist'
    END AS U_Customer_Type_Status
FROM OCRD 
WHERE CardCode = 'SUP00001';

PRINT '';
PRINT '✅ Vendor creation script completed.';
PRINT '';
GO
