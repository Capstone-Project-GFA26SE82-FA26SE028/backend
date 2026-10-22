SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @lockResult int;
EXEC @lockResult = sys.sp_getapplock @Resource = N'InventoryFifoUpgrade',
    @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 30000;
IF @lockResult < 0 THROW 51000, 'Cannot acquire Inventory FIFO upgrade lock.', 1;

IF OBJECT_ID(N'dbo.StockIssue', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StockIssue (
        StockIssueId uniqueidentifier NOT NULL CONSTRAINT PK_StockIssue PRIMARY KEY,
        CreatedByRef uniqueidentifier NOT NULL,
        CreatedAt datetime2 NOT NULL,
        Reason nvarchar(1000) NOT NULL,
        RequestHash varchar(64) NOT NULL
    );
END;
IF COL_LENGTH('dbo.StockMovement', 'StockIssueId') IS NULL
    ALTER TABLE dbo.StockMovement ADD StockIssueId uniqueidentifier NULL
        CONSTRAINT FK_StockMovement_StockIssue REFERENCES dbo.StockIssue(StockIssueId);
-- Dynamic SQL allows the new column to be resolved after ALTER TABLE in this batch.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.StockMovement') AND name = 'IX_StockMovement_StockIssueId')
    EXEC(N'CREATE INDEX IX_StockMovement_StockIssueId ON dbo.StockMovement(StockIssueId)');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.StockMovement') AND name = 'IX_StockMovement_GoodsReceiptItemId')
    CREATE INDEX IX_StockMovement_GoodsReceiptItemId ON dbo.StockMovement(GoodsReceiptItemId);
COMMIT TRANSACTION;
