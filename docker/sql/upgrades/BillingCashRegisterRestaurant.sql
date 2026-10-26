SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.CashRegister', N'U') IS NULL
        THROW 50001, 'Billing CashRegister table is missing. Initialize BillingDb before applying this upgrade.', 1;

    DECLARE @RegisterCount bigint;
    SELECT @RegisterCount = COUNT_BIG(*)
    FROM dbo.CashRegister WITH (TABLOCKX, HOLDLOCK);

    IF COL_LENGTH(N'dbo.CashRegister', N'RestaurantId') IS NULL
    BEGIN
        IF @RegisterCount > 0
            THROW 50001, 'CashRegister contains existing rows without a restaurant reference. Supply an explicit CashRegisterId-to-RestaurantId mapping before upgrading; no branch was assigned and no data was changed.', 1;

        ALTER TABLE dbo.CashRegister ADD RestaurantId uniqueidentifier NOT NULL;
    END
    ELSE
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.CashRegister')
                AND name = N'RestaurantId'
                AND (TYPE_NAME(system_type_id) <> N'uniqueidentifier' OR is_computed = 1)
        )
            THROW 50001, 'CashRegister.RestaurantId must be a non-computed uniqueidentifier. Review the existing schema before upgrading.', 1;

        DECLARE @MissingRestaurant bit;
        EXEC sys.sp_executesql
            N'SELECT @Missing = CASE WHEN EXISTS (SELECT 1 FROM dbo.CashRegister WHERE RestaurantId IS NULL OR RestaurantId = ''00000000-0000-0000-0000-000000000000'') THEN 1 ELSE 0 END;',
            N'@Missing bit OUTPUT',
            @Missing = @MissingRestaurant OUTPUT;

        IF @MissingRestaurant = 1
            THROW 50001, 'CashRegister contains rows without a valid restaurant reference. Supply an explicit mapping before upgrading; no data was changed.', 1;

        IF EXISTS
        (
            SELECT 1
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.CashRegister')
                AND name = N'RestaurantId'
                AND is_nullable = 1
        )
            ALTER TABLE dbo.CashRegister ALTER COLUMN RestaurantId uniqueidentifier NOT NULL;
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
