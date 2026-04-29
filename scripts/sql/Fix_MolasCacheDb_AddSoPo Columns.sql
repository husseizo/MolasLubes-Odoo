ALTER TABLE CacheLiquiMolyReplenishmentRequests ADD SalesOrderDocEntry    INT NULL;
ALTER TABLE CacheLiquiMolyReplenishmentRequests ADD SalesOrderDocNum      INT NULL;
ALTER TABLE CacheLiquiMolyReplenishmentRequests ADD PurchaseOrderDocEntry INT NULL;
ALTER TABLE CacheLiquiMolyReplenishmentRequests ADD PurchaseOrderDocNum   INT NULL;
ALTER TABLE CacheLiquiMolyReplenishmentRequests ADD ExecutionMode NVARCHAR(20) NULL
    CONSTRAINT DF_CacheLM_ExecutionMode DEFAULT 'TRANSFER';
GO
UPDATE CacheLiquiMolyReplenishmentRequests SET ExecutionMode = 'TRANSFER' WHERE ExecutionMode IS NULL;
GO
