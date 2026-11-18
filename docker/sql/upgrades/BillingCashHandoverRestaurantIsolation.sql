SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @LockResult int;
    EXEC @LockResult = sys.sp_getapplock @Resource = N'Billing.CashHandoverRestaurantIsolation',
        @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 30000;
    IF @LockResult < 0
        THROW 51000, 'Cannot acquire the CashHandover restaurant isolation upgrade lock.', 1;

    IF OBJECT_ID(N'dbo.CashHandover', N'U') IS NULL
        OR COL_LENGTH(N'dbo.Bill', N'RestaurantId') IS NULL
        OR COL_LENGTH(N'dbo.CashRegister', N'RestaurantId') IS NULL
        THROW 51000, 'BillingDb with restaurant ownership on Bill and CashRegister is required.', 1;

    IF COL_LENGTH(N'dbo.CashHandover', N'RestaurantId') IS NULL
        ALTER TABLE dbo.CashHandover ADD RestaurantId uniqueidentifier NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.CashHandover') AND name = N'RestaurantId'
            AND system_type_id = 36 AND is_nullable = 1 AND default_object_id = 0 AND is_computed = 0)
        THROW 51000, 'Unexpected CashHandover.RestaurantId definition.', 1;

    -- Only backfill when the bill and the receiving register agree on ownership.
    EXEC sys.sp_executesql N'
        UPDATE handover
        SET RestaurantId = bill.RestaurantId
        FROM dbo.CashHandover AS handover
        JOIN dbo.Payment AS payment ON payment.PaymentId = handover.PaymentId
        JOIN dbo.Bill AS bill ON bill.BillId = payment.BillId
        JOIN dbo.CashShift AS shift ON shift.CashShiftId = payment.CashShiftId
        JOIN dbo.CashRegister AS register ON register.CashRegisterId = shift.CashRegisterId
        WHERE handover.RestaurantId IS NULL
            AND bill.RestaurantId = register.RestaurantId
            AND bill.RestaurantId <> ''00000000-0000-0000-0000-000000000000'';';

    IF NOT EXISTS (SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.CashHandover') AND name = N'IX_CashHandover_RestaurantIsolation')
        EXEC sys.sp_executesql N'CREATE NONCLUSTERED INDEX IX_CashHandover_RestaurantIsolation
            ON dbo.CashHandover(RestaurantId);';

    IF NOT EXISTS (SELECT 1 FROM sys.indexes AS i
        JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1
        JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = OBJECT_ID(N'dbo.CashHandover') AND i.name = N'IX_CashHandover_RestaurantIsolation'
            AND i.type = 2 AND i.is_unique = 0 AND i.has_filter = 0 AND i.is_disabled = 0
            AND c.name = N'RestaurantId' AND ic.is_descending_key = 0
            AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS extra
                WHERE extra.object_id = i.object_id AND extra.index_id = i.index_id
                    AND (extra.key_ordinal > 1 OR extra.is_included_column = 1)))
        THROW 51000, 'Unexpected CashHandover restaurant isolation index definition.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
