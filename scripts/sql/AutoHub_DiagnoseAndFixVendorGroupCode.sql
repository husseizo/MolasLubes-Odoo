-- ========================================================================
-- AutoHub - Diagnose and Fix Vendor GroupCode
-- ========================================================================
-- Purpose:
--   Diagnose whether OCRD.GroupCode for a vendor points to a valid supplier
--   group in OCRG, and repair it by copying a valid GroupCode from an
--   existing supplier if needed.
--
-- Why this exists:
--   SAP Business One validates OCRD.GroupCode against OCRG. If a business
--   partner was inserted directly in SQL without a valid supplier group,
--   opening or updating the BP in SAP can fail with:
--     "business partner group code linked value does not exist"
--
-- Usage:
--   1. Run in MOLAS_Live_2021
--   2. Review the diagnostic output
--   3. If needed, let the script update OCRD.GroupCode automatically
-- ========================================================================

USE MOLAS_Live_2021;
GO

SET NOCOUNT ON;

DECLARE @VendorCode NVARCHAR(50) = 'SUP00001';
DECLARE @ReferenceSupplierCode NVARCHAR(50) = NULL;  -- Optional: set a known-good supplier code
DECLARE @ApplyFix BIT = 1;                           -- 1 = update OCRD.GroupCode when invalid

DECLARE @VendorGroupCode INT = NULL;
DECLARE @ReplacementGroupCode INT = NULL;
DECLARE @HasOcrgGroupType BIT = CASE WHEN COL_LENGTH('dbo.OCRG', 'GroupType') IS NOT NULL THEN 1 ELSE 0 END;
DECLARE @HasOcrgGroupName BIT = CASE WHEN COL_LENGTH('dbo.OCRG', 'GroupName') IS NOT NULL THEN 1 ELSE 0 END;

PRINT '=========================================================================';
PRINT 'AutoHub Vendor GroupCode Diagnostic / Fix';
PRINT '=========================================================================';
PRINT 'Database: MOLAS_Live_2021';
PRINT 'Vendor: ' + @VendorCode;
PRINT '';

IF NOT EXISTS (SELECT 1 FROM OCRD WHERE CardCode = @VendorCode)
BEGIN
    PRINT 'Vendor not found in OCRD.';
    RETURN;
END

SELECT
    @VendorGroupCode = GroupCode
FROM OCRD
WHERE CardCode = @VendorCode;

PRINT '1. Vendor current GroupCode';
PRINT '---------------------------';

SELECT
    CardCode,
    CardName,
    CardType,
    GroupCode,
    LangCode,
    Currency,
    ValidFor,
    FrozenFor
FROM OCRD
WHERE CardCode = @VendorCode;

PRINT '';

PRINT '2. Available supplier groups in OCRG';
PRINT '------------------------------------';

IF OBJECT_ID('dbo.OCRG', 'U') IS NULL
BEGIN
    PRINT 'OCRG not found. Cannot validate GroupCode safely.';
    RETURN;
END

DECLARE @SupplierGroupsSql NVARCHAR(MAX) = N'
    SELECT
        GroupCode'
        + CASE WHEN @HasOcrgGroupName = 1 THEN N', GroupName' ELSE N'' END
        + CASE WHEN @HasOcrgGroupType = 1 THEN N', GroupType' ELSE N'' END
        + N'
    FROM OCRG'
        + CASE WHEN @HasOcrgGroupType = 1 THEN N' WHERE GroupType = ''S''' ELSE N'' END
        + N'
    ORDER BY GroupCode;';

EXEC (@SupplierGroupsSql);

PRINT '';

PRINT '3. Vendor GroupCode link validation';
PRINT '-----------------------------------';

DECLARE @VendorGroupLinkSql NVARCHAR(MAX) = N'
    SELECT
        BP.CardCode,
        BP.GroupCode,
        G.GroupCode AS MatchedGroupCode'
        + CASE WHEN @HasOcrgGroupName = 1 THEN N', G.GroupName' ELSE N'' END
        + CASE WHEN @HasOcrgGroupType = 1 THEN N', G.GroupType' ELSE N'' END
        + N'
    FROM OCRD BP
    LEFT JOIN OCRG G
        ON G.GroupCode = BP.GroupCode'
        + CASE WHEN @HasOcrgGroupType = 1 THEN N' AND G.GroupType = ''S''' ELSE N'' END
        + N'
    WHERE BP.CardCode = @VendorCode;';

EXEC sp_executesql
    @VendorGroupLinkSql,
    N'@VendorCode NVARCHAR(50)',
    @VendorCode = @VendorCode;

PRINT '';

IF EXISTS (
    SELECT 1
    FROM OCRD BP
    INNER JOIN OCRG G
        ON G.GroupCode = BP.GroupCode
       AND (@HasOcrgGroupType = 0 OR G.GroupType = 'S')
    WHERE BP.CardCode = @VendorCode
)
BEGIN
    PRINT 'Vendor GroupCode already points to a valid supplier group.';
    PRINT 'No group-code repair needed.';
    RETURN;
END

PRINT 'Vendor GroupCode is NULL or invalid for suppliers.';
PRINT '';

PRINT '4. Candidate replacement GroupCode';
PRINT '----------------------------------';

IF @ReferenceSupplierCode IS NOT NULL
BEGIN
    SELECT
        @ReplacementGroupCode = BP.GroupCode
    FROM OCRD BP
    INNER JOIN OCRG G
        ON G.GroupCode = BP.GroupCode
       AND (@HasOcrgGroupType = 0 OR G.GroupType = 'S')
    WHERE BP.CardCode = @ReferenceSupplierCode;
END

IF @ReplacementGroupCode IS NULL
BEGIN
    SELECT TOP (1)
        @ReplacementGroupCode = BP.GroupCode
    FROM OCRD BP
    INNER JOIN OCRG G
        ON G.GroupCode = BP.GroupCode
       AND (@HasOcrgGroupType = 0 OR G.GroupType = 'S')
    WHERE BP.CardType = 'S'
      AND BP.CardCode <> @VendorCode
      AND BP.GroupCode IS NOT NULL
    ORDER BY
        CASE WHEN BP.ValidFor = 'Y' THEN 0 ELSE 1 END,
        BP.CardCode;
END

IF @ReplacementGroupCode IS NULL
BEGIN
    DECLARE @FallbackGroupSql NVARCHAR(MAX) = N'
        SELECT TOP (1)
            @ReplacementGroupCode = GroupCode
        FROM OCRG'
            + CASE WHEN @HasOcrgGroupType = 1 THEN N' WHERE GroupType = ''S''' ELSE N'' END
            + N'
        ORDER BY GroupCode;';

    EXEC sp_executesql
        @FallbackGroupSql,
        N'@ReplacementGroupCode INT OUTPUT',
        @ReplacementGroupCode = @ReplacementGroupCode OUTPUT;
END

SELECT
    @ReplacementGroupCode AS ReplacementGroupCode;

DECLARE @ReplacementGroupDetailSql NVARCHAR(MAX) = N'
    SELECT
        GroupCode AS ReplacementGroupCode'
        + CASE WHEN @HasOcrgGroupName = 1 THEN N', GroupName' ELSE N'' END
        + CASE WHEN @HasOcrgGroupType = 1 THEN N', GroupType' ELSE N'' END
        + N'
    FROM OCRG
    WHERE GroupCode = @ReplacementGroupCode;';

EXEC sp_executesql
    @ReplacementGroupDetailSql,
    N'@ReplacementGroupCode INT',
    @ReplacementGroupCode = @ReplacementGroupCode;

PRINT '';

IF @ApplyFix = 1 AND @ReplacementGroupCode IS NOT NULL
BEGIN
    PRINT '5. Applying fix';
    PRINT '----------------';

    UPDATE OCRD
    SET GroupCode = @ReplacementGroupCode
    WHERE CardCode = @VendorCode;

    PRINT 'Vendor GroupCode updated.';
    PRINT '';

    PRINT '6. Verification';
    PRINT '---------------';

    EXEC sp_executesql
        @VendorGroupLinkSql,
        N'@VendorCode NVARCHAR(50)',
        @VendorCode = @VendorCode;
END
ELSE
BEGIN
    PRINT '5. Fix was not applied.';
    PRINT 'Set @ApplyFix = 1 to update OCRD.GroupCode.';
END

PRINT '';
PRINT '=========================================================================';
PRINT 'Done';
PRINT '=========================================================================';

SET NOCOUNT OFF;
GO
