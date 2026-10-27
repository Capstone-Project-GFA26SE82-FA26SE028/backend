SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @LockResult int;
    EXEC @LockResult = sys.sp_getapplock @Resource = N'BillingBillSchemaUpgrade',
        @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 30000;
    IF @LockResult < 0
        THROW 50001, 'Cannot acquire the Billing bill schema upgrade lock.', 1;
    IF OBJECT_ID(N'dbo.Bill', N'U') IS NULL OR OBJECT_ID(N'dbo.BillLine', N'U') IS NULL
        THROW 50001, 'Initialize Billing Bill and BillLine before applying this upgrade.', 1;
    DECLARE @BillCount bigint;
    SELECT @BillCount = COUNT_BIG(*) FROM dbo.Bill WITH (TABLOCKX, HOLDLOCK);
    IF COL_LENGTH(N'dbo.Bill', N'RestaurantId') IS NULL
    BEGIN
        IF @BillCount > 0
            THROW 50001, 'Existing bills require explicit BillId-to-RestaurantId mappings. No restaurant was assigned and no data was changed.', 1;
        ALTER TABLE dbo.Bill ADD RestaurantId uniqueidentifier NOT NULL;
    END
    ELSE
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Bill')
            AND name = N'RestaurantId' AND (TYPE_NAME(system_type_id) <> N'uniqueidentifier' OR is_computed = 1))
            THROW 50001, 'Bill.RestaurantId must be a non-computed uniqueidentifier.', 1;
        DECLARE @Missing bit;
        EXEC sys.sp_executesql N'SELECT @Missing = CASE WHEN EXISTS
            (SELECT 1 FROM dbo.Bill WHERE RestaurantId IS NULL OR RestaurantId = ''00000000-0000-0000-0000-000000000000'')
            THEN 1 ELSE 0 END;', N'@Missing bit OUTPUT', @Missing = @Missing OUTPUT;
        IF @Missing = 1
            THROW 50001, 'Existing bills require valid explicit restaurant mappings. No data was changed.', 1;
        IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Bill') AND name = N'RestaurantId' AND is_nullable = 1)
            ALTER TABLE dbo.Bill ALTER COLUMN RestaurantId uniqueidentifier NOT NULL;
    END;
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.BillLine')
        AND name = N'CogsSnapshot' AND TYPE_NAME(system_type_id) = N'decimal' AND precision = 18 AND scale = 2 AND is_computed = 0)
        THROW 50001, 'BillLine.CogsSnapshot must be decimal(18,2). Review the existing schema.', 1;
    ALTER TABLE dbo.BillLine ALTER COLUMN CogsSnapshot decimal(18,2) NULL;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
