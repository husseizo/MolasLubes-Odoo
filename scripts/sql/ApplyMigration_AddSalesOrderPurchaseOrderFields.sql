/*
================================================================================
  MIGRATION: AddSalesOrderPurchaseOrderFields
  Date: 2025-01-08
  Purpose: Add Sales Order and Purchase Order tracking to replenishment requests
  Database: MOLAS_Live_2021_Cache
  Table: CacheLiquiMolyReplenishmentRequests
  
  This migration supports the inter-company SO→PO→GR workflow by adding:
  - SalesOrderDocEntry/DocNum (MolasLubes ORDR)
  - PurchaseOrderDocEntry/DocNum (AutoHub OPOR)
  - ExecutionMode ("TRANSFER" or "SALES_PURCHASE")
================================================================================
*/

USE MOLAS_Live_2021_Cache;
GO

PRINT '';
PRINT '================================================================================';
PRINT '  Migration: Add Sales Order and Purchase Order Fields';
PRINT '  Table: CacheLiquiMolyReplenishmentRequests';
PRINT '================================================================================';
PRINT '';

-- Check if migration has already been applied
IF EXISTS (
    SELECT 1 
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'CacheLiquiMolyReplenishmentRequests' 
      AND COLUMN_NAME = 'SalesOrderDocEntry'
)
BEGIN
    PRINT '⚠️  WARNING: Migration already applied!';
    PRINT 'Columns SalesOrderDocEntry, SalesOrderDocNum, PurchaseOrderDocEntry,';
    PRINT 'PurchaseOrderDocNum, and ExecutionMode already exist.';
    PRINT '';
    PRINT 'Current column status:';
    
    SELECT 
        COLUMN_NAME,
        DATA_TYPE,
        CHARACTER_MAXIMUM_LENGTH,
        IS_NULLABLE,
        COLUMN_DEFAULT
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'CacheLiquiMolyReplenishmentRequests'
      AND COLUMN_NAME IN (
          'SalesOrderDocEntry',
          'SalesOrderDocNum',
          'PurchaseOrderDocEntry',
          'PurchaseOrderDocNum',
          'ExecutionMode'
      )
    ORDER BY ORDINAL_POSITION;
    
    PRINT '';
    PRINT '❌ MIGRATION ABORTED - Already applied';
    RETURN;
END

PRINT '✅ Migration not yet applied. Proceeding...';
PRINT '';

BEGIN TRY
    BEGIN TRANSACTION;
    
    -- Add Sales Order fields (MolasLubes side)
    PRINT 'Adding SalesOrderDocEntry column...';
    ALTER TABLE CacheLiquiMolyReplenishmentRequests
    ADD SalesOrderDocEntry INT NULL;
    
    PRINT 'Adding SalesOrderDocNum column...';
    ALTER TABLE CacheLiquiMolyReplenishmentRequests
    ADD SalesOrderDocNum INT NULL;
    
    -- Add Purchase Order fields (AutoHub side)
    PRINT 'Adding PurchaseOrderDocEntry column...';
    ALTER TABLE CacheLiquiMolyReplenishmentRequests
    ADD PurchaseOrderDocEntry INT NULL;
    
    PRINT 'Adding PurchaseOrderDocNum column...';
    ALTER TABLE CacheLiquiMolyReplenishmentRequests
    ADD PurchaseOrderDocNum INT NULL;
    
    -- Add document type indicator with default value
    PRINT 'Adding ExecutionMode column...';
    ALTER TABLE CacheLiquiMolyReplenishmentRequests
    ADD ExecutionMode NVARCHAR(20) NULL
    CONSTRAINT DF_ExecutionMode DEFAULT 'TRANSFER';
    
    -- Update existing records to have ExecutionMode = 'TRANSFER'
    PRINT 'Updating existing records with ExecutionMode = ''TRANSFER''...';
    UPDATE CacheLiquiMolyReplenishmentRequests
    SET ExecutionMode = 'TRANSFER'
    WHERE ExecutionMode IS NULL;
    
    COMMIT TRANSACTION;
    
    PRINT '';
    PRINT '✅ Migration applied successfully!';
    PRINT '';
    
    -- Verify columns were added
    PRINT 'Verification - New columns:';
    SELECT 
        COLUMN_NAME,
        DATA_TYPE,
        CHARACTER_MAXIMUM_LENGTH,
        IS_NULLABLE,
        COLUMN_DEFAULT
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'CacheLiquiMolyReplenishmentRequests'
      AND COLUMN_NAME IN (
          'SalesOrderDocEntry',
          'SalesOrderDocNum',
          'PurchaseOrderDocEntry',
          'PurchaseOrderDocNum',
          'ExecutionMode'
      )
    ORDER BY ORDINAL_POSITION;
    
    PRINT '';
    PRINT '================================================================================';
    PRINT '  ✅ MIGRATION COMPLETE!';
    PRINT '================================================================================';
    PRINT '';
    PRINT '📋 NEXT STEPS:';
    PRINT '   1. Update CacheLiquiMolyReplenishmentRequest entity class (add properties)';
    PRINT '   2. Implement PL05PricingCalculator service';
    PRINT '   3. Implement SapInterCompanySalesOrderWriter';
    PRINT '   4. Implement SapPurchaseOrderWriter';
    PRINT '   5. Enhance SapGoodsReceiptWriter for PO-based receipts';
    PRINT '   6. Update LiquiMolyReplenishmentExecutionService';
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
