SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @LockResult int;
    EXEC @LockResult = sys.sp_getapplock @Resource = N'Billing.CheckoutTables.Schema',
        @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 30000;
    IF @LockResult < 0
        THROW 51000, 'Cannot acquire the Billing checkout table upgrade lock.', 1;
    IF OBJECT_ID(N'dbo.Bill', N'U') IS NULL
        THROW 51000, 'Bill is missing. Review the BillingDb baseline before upgrading.', 1;

    IF OBJECT_ID(N'dbo.BillCheckout', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.BillCheckout
        (
            CheckoutId uniqueidentifier NOT NULL CONSTRAINT PK_BillCheckout PRIMARY KEY,
            BillId uniqueidentifier NULL,
            TableSessionRef uniqueidentifier NOT NULL,
            SourceHash char(64) NULL,
            Status varchar(20) NOT NULL,
            CreatedAt datetime2(7) NOT NULL,
            ReleasedAt datetime2(7) NULL,
            RestaurantId uniqueidentifier NULL,
            CONSTRAINT FK_BillCheckout_Bill FOREIGN KEY (BillId) REFERENCES dbo.Bill(BillId)
        );
    END;

    IF COL_LENGTH(N'dbo.BillCheckout', N'RestaurantId') IS NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.BillCheckout WITH (TABLOCKX, HOLDLOCK))
            THROW 51000, 'Existing BillCheckout rows need explicit restaurant mappings before upgrading.', 1;
        ALTER TABLE dbo.BillCheckout ADD RestaurantId uniqueidentifier NULL;
    END;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.BillCheckout')
        AND name = N'UX_BillCheckout_ActiveSession')
        CREATE UNIQUE INDEX UX_BillCheckout_ActiveSession ON dbo.BillCheckout(TableSessionRef) WHERE ReleasedAt IS NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.BillCheckout')
        AND name = N'IX_BillCheckout_RestaurantIsolation')
        EXEC sys.sp_executesql N'CREATE INDEX IX_BillCheckout_RestaurantIsolation ON dbo.BillCheckout(RestaurantId);';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
