-- ========================================================================
-- AutoHub - Diagnose and Fix Vendor Language Code
-- ========================================================================
-- Purpose:
--   Diagnose whether OCRD.LangCode for a vendor points to a valid OLNG row,
--   and repair it by copying a valid LangCode from an existing supplier if
--   needed.
--
-- Why this exists:
--   SAP Business One validates OCRD.LangCode against OLNG. If a business
--   partner was inserted directly in SQL without a valid LangCode, opening
--   or updating the BP in SAP can fail with:
--     "business partner language code linked value does not exist"
--
-- Usage:
--   1. Run in MOLAS_Live_2021
--   2. Review the diagnostic output
--   3. If needed, let the script update OCRD.LangCode automatically
-- ========================================================================

USE MOLAS_Live_2021;
GO

SET NOCOUNT ON;

DECLARE @VendorCode NVARCHAR(50) = 'SUP00001';
DECLARE @ReferenceSupplierCode NVARCHAR(50) = NULL;  -- Optional: set a known-good supplier code
DECLARE @ApplyFix BIT = 1;                           -- 1 = update OCRD.LangCode when invalid

DECLARE @VendorLangCode INT = NULL;
DECLARE @ReplacementLangCode INT = NULL;
DECLARE @HasOlngCode BIT = CASE WHEN COL_LENGTH('dbo.OLNG', 'Code') IS NOT NULL THEN 1 ELSE 0 END;
DECLARE @HasOlngName BIT = CASE WHEN COL_LENGTH('dbo.OLNG', 'Name') IS NOT NULL THEN 1 ELSE 0 END;
DECLARE @HasOlngShortName BIT = CASE WHEN COL_LENGTH('dbo.OLNG', 'ShortName') IS NOT NULL THEN 1 ELSE 0 END;

PRINT '=========================================================================';
PRINT 'AutoHub Vendor Language Code Diagnostic / Fix';
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
    @VendorLangCode = LangCode
FROM OCRD
WHERE CardCode = @VendorCode;

PRINT '1. Vendor current language code';
PRINT '-------------------------------';

SELECT
    CardCode,
    CardName,
    CardType,
    LangCode,
    Currency,
    ValidFor,
    FrozenFor
FROM OCRD
WHERE CardCode = @VendorCode;

PRINT '';

PRINT '2. Available languages in OLNG';
PRINT '------------------------------';

IF OBJECT_ID('dbo.OLNG', 'U') IS NULL OR @HasOlngCode = 0
BEGIN
    PRINT 'OLNG or OLNG.Code not found. Cannot validate LangCode safely.';
    RETURN;
END

DECLARE @OlngSelectSql NVARCHAR(MAX) = N'
    SELECT'
    + CASE WHEN @HasOlngCode = 1 THEN N' Code AS LanguageCode' ELSE N'' END
    + CASE WHEN @HasOlngShortName = 1 THEN N', ShortName' ELSE N'' END
    + CASE WHEN @HasOlngName = 1 THEN N', Name' ELSE N'' END
    + N'
    FROM OLNG
    ORDER BY Code;';

EXEC (@OlngSelectSql);

PRINT '';

PRINT '3. Vendor language link validation';
PRINT '----------------------------------';

DECLARE @VendorLanguageLinkSql NVARCHAR(MAX) = N'
    SELECT
        BP.CardCode,
        BP.LangCode,
        L.Code AS MatchedLanguageCode'
        + CASE WHEN @HasOlngShortName = 1 THEN N', L.ShortName' ELSE N'' END
        + CASE WHEN @HasOlngName = 1 THEN N', L.Name' ELSE N'' END
        + N'
    FROM OCRD BP
    LEFT JOIN OLNG L
        ON L.Code = BP.LangCode
    WHERE BP.CardCode = @VendorCode;';

EXEC sp_executesql
    @VendorLanguageLinkSql,
    N'@VendorCode NVARCHAR(50)',
    @VendorCode = @VendorCode;

PRINT '';

IF EXISTS (
    SELECT 1
    FROM OCRD BP
    INNER JOIN OLNG L
        ON L.Code = BP.LangCode
    WHERE BP.CardCode = @VendorCode
)
BEGIN
    PRINT 'Vendor LangCode already points to a valid OLNG row.';
    PRINT 'No language-code repair needed.';
    RETURN;
END

PRINT 'Vendor LangCode is NULL or invalid.';
PRINT '';

PRINT '4. Candidate replacement language code';
PRINT '--------------------------------------';

IF @ReferenceSupplierCode IS NOT NULL
BEGIN
    SELECT
        @ReplacementLangCode = BP.LangCode
    FROM OCRD BP
    INNER JOIN OLNG L
        ON L.Code = BP.LangCode
    WHERE BP.CardCode = @ReferenceSupplierCode;
END

IF @ReplacementLangCode IS NULL
BEGIN
    SELECT TOP (1)
        @ReplacementLangCode = BP.LangCode
    FROM OCRD BP
    INNER JOIN OLNG L
        ON L.Code = BP.LangCode
    WHERE BP.CardType = 'S'
      AND BP.CardCode <> @VendorCode
      AND BP.LangCode IS NOT NULL
    ORDER BY
        CASE WHEN BP.ValidFor = 'Y' THEN 0 ELSE 1 END,
        BP.CardCode;
END

IF @ReplacementLangCode IS NULL
BEGIN
    SELECT TOP (1)
        @ReplacementLangCode = Code
    FROM OLNG
    ORDER BY Code;
END

SELECT
    @ReplacementLangCode AS ReplacementLangCode;

DECLARE @ReplacementLanguageSql NVARCHAR(MAX) = N'
    SELECT
        Code AS ReplacementLangCode'
        + CASE WHEN @HasOlngShortName = 1 THEN N', ShortName' ELSE N'' END
        + CASE WHEN @HasOlngName = 1 THEN N', Name' ELSE N'' END
        + N'
    FROM OLNG
    WHERE Code = @ReplacementLangCode;';

EXEC sp_executesql
    @ReplacementLanguageSql,
    N'@ReplacementLangCode INT',
    @ReplacementLangCode = @ReplacementLangCode;

PRINT '';

IF @ApplyFix = 1 AND @ReplacementLangCode IS NOT NULL
BEGIN
    PRINT '5. Applying fix';
    PRINT '----------------';

    UPDATE OCRD
    SET LangCode = @ReplacementLangCode
    WHERE CardCode = @VendorCode;

    PRINT 'Vendor LangCode updated.';
    PRINT '';

    PRINT '6. Verification';
    PRINT '---------------';

    EXEC sp_executesql
        @VendorLanguageLinkSql,
        N'@VendorCode NVARCHAR(50)',
        @VendorCode = @VendorCode;
END
ELSE
BEGIN
    PRINT '5. Fix was not applied.';
    PRINT 'Set @ApplyFix = 1 to update OCRD.LangCode.';
END

PRINT '';
PRINT '=========================================================================';
PRINT 'Done';
PRINT '=========================================================================';

SET NOCOUNT OFF;
GO
