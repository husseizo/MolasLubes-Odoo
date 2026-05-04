-- ========================================================================
-- AutoHub - Diagnose Vendor / Warehouse / Branch Alignment
-- ========================================================================
-- Purpose:
--   1. Show the branch assigned to the target warehouse
--   2. Show the configured AutoHub profile branch from appsettings
--   3. Show the vendor's branch assignment status
--
-- Usage:
--   - Execute in MOLAS_Live_2021
--   - Update the variables below before running
-- ========================================================================

USE MOLAS_Live_2021;
GO

SET NOCOUNT ON;

DECLARE @VendorCode NVARCHAR(50) = 'SUP00001';
DECLARE @TargetWarehouse NVARCHAR(20) = '01';
DECLARE @ConfiguredProfileBranchId INT = 1; -- Keep this in sync with AutoHub.Sap.BranchId in appsettings

DECLARE @WarehouseBranchId INT = NULL;
DECLARE @WarehouseBranchName NVARCHAR(200) = NULL;
DECLARE @ConfiguredProfileBranchName NVARCHAR(200) = NULL;

PRINT '=========================================================================';
PRINT 'AutoHub Vendor / Warehouse / Branch Diagnostic';
PRINT '=========================================================================';
PRINT 'Database: MOLAS_Live_2021';
PRINT 'Vendor: ' + @VendorCode;
PRINT 'Target Warehouse: ' + @TargetWarehouse;
PRINT 'Configured AutoHub Profile BranchId: ' + CAST(@ConfiguredProfileBranchId AS NVARCHAR(20));
PRINT '';

-- ------------------------------------------------------------------------
-- 1. Target warehouse -> branch
-- ------------------------------------------------------------------------
PRINT '1. Target warehouse branch';
PRINT '--------------------------';

SELECT
    @WarehouseBranchId = W.BPLid,
    @WarehouseBranchName = B.BPLName
FROM OWHS W
LEFT JOIN OBPL B
    ON B.BPLId = W.BPLid
WHERE W.WhsCode = @TargetWarehouse;

SELECT
    W.WhsCode,
    W.WhsName,
    W.BPLid AS WarehouseBranchId,
    B.BPLName AS WarehouseBranchName
FROM OWHS W
LEFT JOIN OBPL B
    ON B.BPLId = W.BPLid
WHERE W.WhsCode = @TargetWarehouse;

IF @WarehouseBranchId IS NULL
BEGIN
    PRINT 'WARNING: Warehouse not found or no branch assigned.';
END

PRINT '';

-- ------------------------------------------------------------------------
-- 2. Configured AutoHub profile branch
-- ------------------------------------------------------------------------
PRINT '2. Configured AutoHub profile branch';
PRINT '------------------------------------';

SELECT
    @ConfiguredProfileBranchName = BPLName
FROM OBPL
WHERE BPLId = @ConfiguredProfileBranchId;

SELECT
    BPLId,
    BPLName,
    MainBPL
FROM OBPL
WHERE BPLId = @ConfiguredProfileBranchId;

IF @ConfiguredProfileBranchName IS NULL
BEGIN
    PRINT 'WARNING: Configured profile branch was not found in OBPL.';
END

PRINT '';

-- ------------------------------------------------------------------------
-- 3. Vendor basic status
-- ------------------------------------------------------------------------
PRINT '3. Vendor basic status';
PRINT '----------------------';

SELECT
    CardCode,
    CardName,
    CardType,
    Currency,
    ValidFor,
    FrozenFor
FROM OCRD
WHERE CardCode = @VendorCode;

IF NOT EXISTS (SELECT 1 FROM OCRD WHERE CardCode = @VendorCode)
BEGIN
    PRINT 'WARNING: Vendor does not exist in OCRD.';
END

PRINT '';

-- ------------------------------------------------------------------------
-- 4. Vendor branch assignment rows
-- ------------------------------------------------------------------------
PRINT '4. Vendor branch assignment rows';
PRINT '--------------------------------';

IF OBJECT_ID('dbo.CRD8', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.CRD8', 'CardCode') IS NOT NULL
   AND COL_LENGTH('dbo.CRD8', 'BPLId') IS NOT NULL
BEGIN
    DECLARE @BranchRowsSql NVARCHAR(MAX) = N'
        SELECT
            C.CardCode,
            C.BPLId'
            + CASE WHEN COL_LENGTH('dbo.CRD8', 'Disabled') IS NOT NULL THEN N', C.Disabled' ELSE N'' END
            + CASE WHEN COL_LENGTH('dbo.CRD8', 'DisableForBP') IS NOT NULL THEN N', C.DisableForBP' ELSE N'' END
            + CASE WHEN COL_LENGTH('dbo.CRD8', 'MainBPL') IS NOT NULL THEN N', C.MainBPL' ELSE N'' END
            + N', B.BPLName
        FROM CRD8 C
        LEFT JOIN OBPL B
            ON B.BPLId = C.BPLId
        WHERE C.CardCode = @VendorCode
        ORDER BY C.BPLId;';

    EXEC sp_executesql
        @BranchRowsSql,
        N'@VendorCode NVARCHAR(50)',
        @VendorCode = @VendorCode;
END
ELSE
BEGIN
    PRINT 'CRD8 branch-assignment table/columns were not found as expected in this database.';
    PRINT 'Inspect BP branch assignment in SAP Business One UI for this vendor.';
END

PRINT '';

-- ------------------------------------------------------------------------
-- 5. Summary checks
-- ------------------------------------------------------------------------
PRINT '5. Summary checks';
PRINT '-----------------';

SELECT
    @TargetWarehouse AS TargetWarehouse,
    @WarehouseBranchId AS WarehouseBranchId,
    @WarehouseBranchName AS WarehouseBranchName,
    @ConfiguredProfileBranchId AS ConfiguredProfileBranchId,
    @ConfiguredProfileBranchName AS ConfiguredProfileBranchName,
    CASE
        WHEN @WarehouseBranchId IS NULL THEN 'UNKNOWN'
        WHEN @WarehouseBranchId = @ConfiguredProfileBranchId THEN 'MATCH'
        ELSE 'MISMATCH'
    END AS WarehouseVsConfiguredBranch;

IF OBJECT_ID('dbo.CRD8', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.CRD8', 'CardCode') IS NOT NULL
   AND COL_LENGTH('dbo.CRD8', 'BPLId') IS NOT NULL
BEGIN
    SELECT
        @VendorCode AS VendorCode,
        CASE
            WHEN EXISTS (
                SELECT 1
                FROM CRD8
                WHERE CardCode = @VendorCode
                  AND BPLId = @ConfiguredProfileBranchId
            ) THEN 'YES'
            ELSE 'NO'
        END AS AssignedToConfiguredProfileBranch,
        CASE
            WHEN @WarehouseBranchId IS NULL THEN 'UNKNOWN'
            WHEN EXISTS (
                SELECT 1
                FROM CRD8
                WHERE CardCode = @VendorCode
                  AND BPLId = @WarehouseBranchId
            ) THEN 'YES'
            ELSE 'NO'
        END AS AssignedToWarehouseBranch;
END
ELSE
BEGIN
    PRINT 'Vendor branch summary could not be derived automatically because CRD8 was unavailable.';
END

PRINT '';
PRINT '=========================================================================';
PRINT 'Diagnostic complete';
PRINT '=========================================================================';

SET NOCOUNT OFF;
GO
