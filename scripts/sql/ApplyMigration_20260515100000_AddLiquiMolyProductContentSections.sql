IF COL_LENGTH('dbo.CacheLiquiMolyProducts', 'Application') IS NULL
BEGIN
    ALTER TABLE dbo.CacheLiquiMolyProducts
        ADD Application NVARCHAR(MAX) NULL;
END;

IF COL_LENGTH('dbo.CacheLiquiMolyProducts', 'LiquiMolyRecommendations') IS NULL
BEGIN
    ALTER TABLE dbo.CacheLiquiMolyProducts
        ADD LiquiMolyRecommendations NVARCHAR(MAX) NULL;
END;

IF COL_LENGTH('dbo.CacheLiquiMolyProducts', 'SpecificationItems') IS NULL
BEGIN
    ALTER TABLE dbo.CacheLiquiMolyProducts
        ADD SpecificationItems NVARCHAR(MAX) NULL;
END;
