IF COL_LENGTH('dbo.CacheLiquiMolyProducts', 'PrimaryBarcode') IS NULL
BEGIN
    ALTER TABLE dbo.CacheLiquiMolyProducts
        ADD PrimaryBarcode NVARCHAR(50) NULL;
END;

IF COL_LENGTH('dbo.CacheLiquiMolyProducts', 'PrimaryBarcodeUomCode') IS NULL
BEGIN
    ALTER TABLE dbo.CacheLiquiMolyProducts
        ADD PrimaryBarcodeUomCode NVARCHAR(20) NULL;
END;

IF COL_LENGTH('dbo.CacheLiquiMolyProducts', 'PrimaryBarcodeUomName') IS NULL
BEGIN
    ALTER TABLE dbo.CacheLiquiMolyProducts
        ADD PrimaryBarcodeUomName NVARCHAR(100) NULL;
END;

IF COL_LENGTH('dbo.CacheLiquiMolyProducts', 'PrimaryBarcodeUomEntry') IS NULL
BEGIN
    ALTER TABLE dbo.CacheLiquiMolyProducts
        ADD PrimaryBarcodeUomEntry INT NULL;
END;

IF COL_LENGTH('dbo.CacheLiquiMolyProducts', 'PrimaryBarcodeBaseQtyInGroup') IS NULL
BEGIN
    ALTER TABLE dbo.CacheLiquiMolyProducts
        ADD PrimaryBarcodeBaseQtyInGroup DECIMAL(19,6) NULL;
END;

IF COL_LENGTH('dbo.CacheLiquiMolyProducts', 'HasUnitBarcode') IS NULL
BEGIN
    ALTER TABLE dbo.CacheLiquiMolyProducts
        ADD HasUnitBarcode BIT NOT NULL
            CONSTRAINT DF_CacheLiquiMolyProducts_HasUnitBarcode DEFAULT (0);
END;

IF COL_LENGTH('dbo.CacheLiquiMolyProducts', 'BarcodeResolutionStatus') IS NULL
BEGIN
    ALTER TABLE dbo.CacheLiquiMolyProducts
        ADD BarcodeResolutionStatus NVARCHAR(50) NULL;
END;

IF COL_LENGTH('dbo.CacheLiquiMolyProducts', 'BarcodeResolutionNote') IS NULL
BEGIN
    ALTER TABLE dbo.CacheLiquiMolyProducts
        ADD BarcodeResolutionNote NVARCHAR(500) NULL;
END;

IF COL_LENGTH('dbo.CacheLiquiMolyProducts', 'AllBarcodes') IS NULL
BEGIN
    ALTER TABLE dbo.CacheLiquiMolyProducts
        ADD AllBarcodes NVARCHAR(MAX) NULL;
END;

IF COL_LENGTH('dbo.CacheLiquiMolyProducts', 'SapUomInfo') IS NULL
BEGIN
    ALTER TABLE dbo.CacheLiquiMolyProducts
        ADD SapUomInfo NVARCHAR(MAX) NULL;
END;
