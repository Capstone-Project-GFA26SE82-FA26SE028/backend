SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @LockResult int;
    EXEC @LockResult = sys.sp_getapplock @Resource = N'Billing.CheckoutSubmission',
        @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 30000;
    IF @LockResult < 0
        THROW 51000, 'Cannot acquire the Billing checkout submission upgrade lock.', 1;

    IF OBJECT_ID(N'dbo.BillCheckout', N'U') IS NULL OR OBJECT_ID(N'dbo.CashRegister', N'U') IS NULL
        THROW 51000, 'The current BillingDb schema is required before applying this upgrade.', 1;

    IF NOT EXISTS (SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.CashRegister') AND name = N'RestaurantId'
            AND system_type_id = 36 AND is_nullable = 0 AND is_computed = 0)
        THROW 51000, 'Apply the CashRegister restaurant mapping upgrade first.', 1;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.CashRegister') AND name = N'UX_CashRegister_Restaurant')
    BEGIN
        IF EXISTS (SELECT RestaurantId FROM dbo.CashRegister WITH (TABLOCKX, HOLDLOCK)
            GROUP BY RestaurantId HAVING COUNT(*) > 1)
            THROW 51000, 'Multiple cash registers exist for a restaurant. Resolve the duplicate mappings before upgrading. No rows were changed.', 1;

        CREATE UNIQUE NONCLUSTERED INDEX UX_CashRegister_Restaurant ON dbo.CashRegister(RestaurantId);
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes AS i
        JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1
        JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = OBJECT_ID(N'dbo.CashRegister') AND i.name = N'UX_CashRegister_Restaurant'
            AND i.is_unique = 1 AND i.has_filter = 0 AND i.is_disabled = 0 AND c.name = N'RestaurantId'
            AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS additional
                WHERE additional.object_id = i.object_id AND additional.index_id = i.index_id AND additional.key_ordinal > 1))
        THROW 51000, 'UX_CashRegister_Restaurant must guarantee one register per restaurant.', 1;

    IF COL_LENGTH(N'dbo.BillCheckout', N'CashRegisterId') IS NULL
        ALTER TABLE dbo.BillCheckout ADD CashRegisterId uniqueidentifier NULL;

    IF COL_LENGTH(N'dbo.BillCheckout', N'SubmittedAt') IS NULL
        ALTER TABLE dbo.BillCheckout ADD SubmittedAt datetime2(7) NULL;

    IF NOT EXISTS (SELECT 1
        FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.BillCheckout')
            AND name = N'FK_BillCheckout_CashRegister')
    BEGIN
        ALTER TABLE dbo.BillCheckout WITH CHECK ADD CONSTRAINT FK_BillCheckout_CashRegister
            FOREIGN KEY (CashRegisterId) REFERENCES dbo.CashRegister(CashRegisterId);
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
