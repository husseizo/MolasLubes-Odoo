-- Creates CacheLiquiMolyTransfers and CacheLiquiMolyTransferLines in MolasCacheDb
-- Run against: MolasCacheDb

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CacheLiquiMolyTransfers')
BEGIN
    CREATE TABLE [dbo].[CacheLiquiMolyTransfers] (
        [Id]                   INT            NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [TransferRef]          NVARCHAR(20)   NOT NULL,
        [SourceProfile]        NVARCHAR(50)   NOT NULL,
        [TargetProfile]        NVARCHAR(50)   NOT NULL,
        [SourceWarehouse]      NVARCHAR(20)   NOT NULL,
        [TargetWarehouse]      NVARCHAR(20)   NOT NULL,
        [Comments]             NVARCHAR(500)  NULL,
        [GoodsIssueDocEntry]   INT            NULL,
        [GoodsIssueDocNum]     NVARCHAR(20)   NULL,
        [GoodsReceiptDocEntry] INT            NULL,
        [GoodsReceiptDocNum]   NVARCHAR(20)   NULL,
        [Status]               NVARCHAR(20)   NOT NULL DEFAULT 'PENDING',
        [ErrorMessage]         NVARCHAR(1000) NULL,
        [CreatedAt]            DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
        [CompletedAt]          DATETIME2      NULL
    );
    PRINT 'Created CacheLiquiMolyTransfers';
END
ELSE
    PRINT 'CacheLiquiMolyTransfers already exists';

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CacheLiquiMolyTransferLines')
BEGIN
    CREATE TABLE [dbo].[CacheLiquiMolyTransferLines] (
        [Id]             INT            NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [TransferId]     INT            NOT NULL,
        [SourceItemCode] NVARCHAR(50)   NOT NULL,
        [TargetItemCode] NVARCHAR(50)   NOT NULL,
        [ArticleNumber]  NVARCHAR(50)   NOT NULL,
        [SourceItemName] NVARCHAR(200)  NULL,
        [TargetItemName] NVARCHAR(200)  NULL,
        [Quantity]       DECIMAL(18,4)  NOT NULL,
        [Status]         NVARCHAR(20)   NOT NULL DEFAULT 'PENDING',
        [ErrorMessage]   NVARCHAR(1000) NULL,
        CONSTRAINT [FK_CacheLiquiMolyTransferLines_TransferId]
            FOREIGN KEY ([TransferId]) REFERENCES [CacheLiquiMolyTransfers]([Id])
    );
    PRINT 'Created CacheLiquiMolyTransferLines';
END
ELSE
    PRINT 'CacheLiquiMolyTransferLines already exists';
