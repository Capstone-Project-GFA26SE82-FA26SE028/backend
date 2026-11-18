SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @LockResult int;
    EXEC @LockResult = sys.sp_getapplock @Resource = N'Billing.CheckoutPreparation',
        @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 30000;
    IF @LockResult < 0
        THROW 51000, 'Cannot acquire the Billing checkout preparation upgrade lock.', 1;

    IF COL_LENGTH(N'dbo.BillCheckout', N'CashRegisterId') IS NULL
        OR COL_LENGTH(N'dbo.BillCheckout', N'SubmittedAt') IS NULL
        THROW 51000, 'Apply BillingCheckoutSubmission.sql before the checkout preparation upgrade.', 1;

    IF COL_LENGTH(N'dbo.BillCheckout', N'PreparedByRef') IS NULL
        ALTER TABLE dbo.BillCheckout ADD PreparedByRef uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.BillCheckout', N'PreparedAt') IS NULL
        ALTER TABLE dbo.BillCheckout ADD PreparedAt datetime2(7) NULL;
    IF COL_LENGTH(N'dbo.BillCheckout', N'PresentedAt') IS NULL
        ALTER TABLE dbo.BillCheckout ADD PresentedAt datetime2(7) NULL;

    IF (SELECT COUNT(*) FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.BillCheckout') AND is_nullable = 1
            AND is_computed = 0 AND default_object_id = 0
            AND ((name = N'PreparedByRef' AND system_type_id = 36)
                OR (name IN (N'PreparedAt', N'PresentedAt') AND system_type_id = 42 AND scale = 7))) <> 3
        THROW 51000, 'Unexpected Billing checkout preparation column definitions.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
