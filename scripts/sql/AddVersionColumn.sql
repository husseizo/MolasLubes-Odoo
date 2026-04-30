IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'CacheLiquiMolyReplenishmentRequests'
      AND COLUMN_NAME = 'Version')
BEGIN
    ALTER TABLE CacheLiquiMolyReplenishmentRequests
        ADD Version INT NOT NULL CONSTRAINT DF_CacheLM_Version DEFAULT 1;
END
GO
UPDATE CacheLiquiMolyReplenishmentRequests SET Version = 1 WHERE Version IS NULL;
GO
