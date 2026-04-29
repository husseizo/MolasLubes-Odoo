/*
================================================================================
  MIGRATION: AddLiquiMolyReplenishmentRequests  
  Migration ID: 20260327120000
  Purpose: Create tables for LiquiMoly replenishment workflow
  Database: MOLAS_Live_2021_Cache
  
  Creates:
  - CacheLiquiMolyReplenishmentRequests (header table)
  - CacheLiquiMolyReplenishmentRequestLines (line items)
================================================================================
*/

USE MOLAS_Live_2021_Cache;
GO

PRINT '';
PRINT '================================================================================';
PRINT '  Migration: AddLiquiMolyReplenishmentRequests';
PRINT '  Creating workflow tables for Liqui Moly replenishment';
PRINT '================================================================================';
PRINT '';

-- Check if migration has already been applied
IF EXISTS (
    SELECT 1 
    FROM INFORMATION_SCHEMA.TABLES 
    WHERE TABLE_NAME = 'CacheLiquiMolyReplenishmentRequests'
)
BEGIN
    PRINT '⚠️  WARNING: Tables already exist!';
    PRINT 'CacheLiquiMolyReplenishmentRequests table already exists.';
    PRINT '';
    PRINT '❌ MIGRATION ABORTED - Already applied';
    RETURN;
END

PRINT '✅ Tables do not exist. Proceeding with creation...';
PRINT '';

BEGIN TRY
    BEGIN TRANSACTION;
    
    -- Create CacheLiquiMolyReplenishmentRequests table
    PRINT 'Creating CacheLiquiMolyReplenishmentRequests table...';
    CREATE TABLE CacheLiquiMolyReplenishmentRequests (
        Id INT IDENTITY(1,1) NOT NULL,
        RequestRef NVARCHAR(30) NOT NULL,
        SourceProfile NVARCHAR(50) NOT NULL,
        TargetProfile NVARCHAR(50) NOT NULL,
        SourceWarehouse NVARCHAR(20) NOT NULL,
        TargetWarehouse NVARCHAR(20) NOT NULL,
        Status NVARCHAR(20) NOT NULL,
        RequestedBySapUser NVARCHAR(50) NULL,
        ApprovedBySapUser NVARCHAR(50) NULL,
        RejectedBySapUser NVARCHAR(50) NULL,
        ExecutedBySapUser NVARCHAR(50) NULL,
        CreatedAt DATETIME2 NOT NULL,
        SubmittedAt DATETIME2 NULL,
        ApprovedAt DATETIME2 NULL,
        RejectedAt DATETIME2 NULL,
        ExecutedAt DATETIME2 NULL,
        Comments NVARCHAR(500) NULL,
        RejectionReason NVARCHAR(500) NULL,
        TransferRef NVARCHAR(30) NULL,
        GoodsIssueDocEntry INT NULL,
        GoodsIssueDocNum NVARCHAR(20) NULL,
        GoodsReceiptDocEntry INT NULL,
        GoodsReceiptDocNum NVARCHAR(20) NULL,
        ErrorMessage NVARCHAR(1000) NULL,
        CONSTRAINT PK_CacheLiquiMolyReplenishmentRequests PRIMARY KEY (Id)
    );
    
    -- Create CacheLiquiMolyReplenishmentRequestLines table
    PRINT 'Creating CacheLiquiMolyReplenishmentRequestLines table...';
    CREATE TABLE CacheLiquiMolyReplenishmentRequestLines (
        Id INT IDENTITY(1,1) NOT NULL,
        RequestId INT NOT NULL,
        SourceItemCode NVARCHAR(50) NOT NULL,
        TargetItemCode NVARCHAR(50) NOT NULL,
        ArticleNumber NVARCHAR(20) NOT NULL,
        ItemName NVARCHAR(200) NULL,
        CurrentStockTarget DECIMAL(18,4) NOT NULL,
        AvailableSupplierStock DECIMAL(18,4) NOT NULL,
        QtySold30d DECIMAL(18,4) NOT NULL,
        QtySold60d DECIMAL(18,4) NOT NULL,
        QtySold90d DECIMAL(18,4) NOT NULL,
        AvgDailySales30d DECIMAL(18,4) NOT NULL,
        DaysOfStock DECIMAL(18,2) NOT NULL,
        SuggestedQty DECIMAL(18,4) NOT NULL,
        TrendCategory NVARCHAR(20) NULL,
        Priority INT NOT NULL,
        ApprovedQty DECIMAL(18,4) NULL,
        ExecutionStatus NVARCHAR(20) NOT NULL,
        ExecutionMessage NVARCHAR(500) NULL,
        CONSTRAINT PK_CacheLiquiMolyReplenishmentRequestLines PRIMARY KEY (Id),
        CONSTRAINT FK_CacheLiquiMolyReplenishmentRequestLines_CacheLiquiMolyReplenishmentRequests_RequestId 
            FOREIGN KEY (RequestId) REFERENCES CacheLiquiMolyReplenishmentRequests(Id) ON DELETE CASCADE
    );
    
    -- Create indexes
    PRINT 'Creating indexes...';
    CREATE UNIQUE INDEX IX_CacheLMReplenishmentRequests_RequestRef 
        ON CacheLiquiMolyReplenishmentRequests(RequestRef);
        
    CREATE INDEX IX_CacheLMReplenishmentRequests_Status 
        ON CacheLiquiMolyReplenishmentRequests(Status);
        
    CREATE INDEX IX_CacheLMReplenishmentRequestLines_RequestId 
        ON CacheLiquiMolyReplenishmentRequestLines(RequestId);
    
    -- Add migration record
    PRINT 'Recording migration in __EFMigrationsHistory_Live2021Cache...';
    INSERT INTO __EFMigrationsHistory_Live2021Cache (MigrationId, ProductVersion)
    VALUES ('20260327120000_AddLiquiMolyReplenishmentRequests', '10.0.2');
    
    COMMIT TRANSACTION;
    
    PRINT '';
    PRINT '✅ Tables and indexes created successfully!';
    PRINT '';
    
    -- Verify
    PRINT 'Verification - Created tables:';
    SELECT TABLE_NAME 
    FROM INFORMATION_SCHEMA.TABLES 
    WHERE TABLE_NAME LIKE '%LiquiMolyReplenishment%'
    ORDER BY TABLE_NAME;
    
    PRINT '';
    PRINT '================================================================================';
    PRINT '  ✅ MIGRATION COMPLETE!';
    PRINT '================================================================================';
    PRINT '';
    PRINT '📋 NEXT STEP:';
    PRINT '   Run ApplyMigration_AddSalesOrderPurchaseOrderFields.sql';
    PRINT '   to add SO/PO tracking columns';
    PRINT '';
    
END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION;
    
    PRINT '';
    PRINT '❌ MIGRATION FAILED!';
    PRINT 'Error Number: ' + CAST(ERROR_NUMBER() AS NVARCHAR(10));
    PRINT 'Error Message: ' + ERROR_MESSAGE();
    PRINT 'Error Line: ' + CAST(ERROR_LINE() AS NVARCHAR(10));
    PRINT '';
    
    THROW;
END CATCH

GO
