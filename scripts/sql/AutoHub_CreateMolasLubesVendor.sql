-- ========================================================================
-- AutoHub (MOLAS_Live_2021) - Create Vendor for MolasLubes
-- ========================================================================
-- Purpose: Create vendor master data for inter-company purchase orders
-- Company: MolasLubes Ltd (Molas_Lubes_LTD)
-- Vendor Code: SUP00001
-- Execute this in MOLAS_Live_2021 database BEFORE implementing SO/PO flow
-- ========================================================================

USE MOLAS_Live_2021;
GO

-- Step 1: Check if vendor already exists
IF EXISTS (SELECT 1 FROM OCRD WHERE CardCode = 'SUP00001')
BEGIN
    PRINT '⚠️  Vendor SUP00001 already exists. Skipping creation.';
END
ELSE
BEGIN
    PRINT '✅ Creating vendor SUP00001 "Molas Lubes Ltd"...';

    -- Step 2: Get next DocEntry value
    DECLARE @NextDocEntry INT;
    SELECT @NextDocEntry = ISNULL(MAX(DocEntry), 0) + 1 FROM OCRD;

    PRINT '📝 Using DocEntry: ' + CAST(@NextDocEntry AS VARCHAR);

    -- Step 3: Enable identity insert and create vendor
    SET IDENTITY_INSERT OCRD ON;

    INSERT INTO OCRD (
        DocEntry,           -- Required identity field
        CardCode,
        CardName,
        CardType,           -- 'S' = Supplier (Vendor)
        Currency,           -- TZS (Tanzanian Shilling)
        ValidFor,           -- 'Y' = Active
        FrozenFor,          -- 'N' = Not frozen
        U_Customer_Type     -- UDF (shortened value to avoid truncation)
    )
    VALUES (
        @NextDocEntry,                  -- DocEntry (managed)
        'SUP00001',                     -- CardCode
        'Molas Lubes Ltd',              -- CardName
        'S',                            -- CardType (Supplier)
        'TZS',                          -- Currency
        'Y',                            -- ValidFor (Active)
        'N',                            -- FrozenFor (Not frozen)
        'reseller'                      -- U_Customer_Type (shortened to avoid truncation)
    );

    SET IDENTITY_INSERT OCRD OFF;

    PRINT '✅ Vendor SUP00001 created successfully.';
END
GO

-- Step 3: Verification query
PRINT '';
PRINT '📋 Verification - Vendor Details:';
PRINT '═══════════════════════════════════════════════════════════════';

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
PRINT '✅ Vendor creation script completed.';
PRINT '';
PRINT '📝 NOTES:';
PRINT '  - Verify GroupCode (vendor group) matches your AutoHub configuration';
PRINT '  - Purchase Orders will reference this vendor: SUP00001';
PRINT '  - Sales Orders in MolasLubes will reference customer: SHP00118';
PRINT '  - Both companies use LIQUI MOLY brand items';
PRINT '';
GO
