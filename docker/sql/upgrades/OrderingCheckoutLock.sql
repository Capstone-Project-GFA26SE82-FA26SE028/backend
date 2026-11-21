SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @LockResult int;
    EXEC @LockResult = sys.sp_getapplock @Resource = N'Ordering.CheckoutLock.Schema',
        @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 30000;
    IF @LockResult < 0
        THROW 51000, 'Cannot acquire the Ordering checkout lock upgrade lock.', 1;

    IF OBJECT_ID(N'dbo.CheckoutLock', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.CheckoutLock
        (
            CheckoutId uniqueidentifier NOT NULL CONSTRAINT PK_CheckoutLock PRIMARY KEY,
            BillIdRef uniqueidentifier NULL,
            TableSessionRef uniqueidentifier NOT NULL,
            SourceHash char(64) NULL,
            Status varchar(20) NOT NULL,
            CreatedAt datetime2(7) NOT NULL,
            ReleasedAt datetime2(7) NULL,
            RestaurantId uniqueidentifier NULL
        );
    END;

    IF COL_LENGTH(N'dbo.CheckoutLock', N'RestaurantId') IS NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.CheckoutLock WITH (TABLOCKX, HOLDLOCK))
            THROW 51000, 'Existing CheckoutLock rows need explicit restaurant mappings before upgrading.', 1;
        ALTER TABLE dbo.CheckoutLock ADD RestaurantId uniqueidentifier NULL;
    END;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CheckoutLock')
        AND name = N'UX_CheckoutLock_ActiveSession')
        CREATE UNIQUE INDEX UX_CheckoutLock_ActiveSession ON dbo.CheckoutLock(TableSessionRef) WHERE ReleasedAt IS NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CheckoutLock')
        AND name = N'IX_CheckoutLock_RestaurantIsolation')
        EXEC sys.sp_executesql N'CREATE INDEX IX_CheckoutLock_RestaurantIsolation ON dbo.CheckoutLock(RestaurantId);';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
