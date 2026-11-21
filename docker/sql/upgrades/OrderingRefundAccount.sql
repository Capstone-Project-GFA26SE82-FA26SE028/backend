SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @LockResult int;
    EXEC @LockResult = sys.sp_getapplock @Resource = N'Ordering.RefundAccount.Schema',
        @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 30000;
    IF @LockResult < 0
        THROW 51000, 'Cannot acquire the Ordering refund account upgrade lock.', 1;
    IF OBJECT_ID(N'dbo.[Order]', N'U') IS NULL
        THROW 51000, 'Order is missing. Review the OrderingDb baseline before upgrading.', 1;

    IF COL_LENGTH(N'dbo.[Order]', N'RefundBankName') IS NULL
        ALTER TABLE dbo.[Order] ADD RefundBankName nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.[Order]', N'RefundAccountNumber') IS NULL
        ALTER TABLE dbo.[Order] ADD RefundAccountNumber nvarchar(34) NULL;
    IF COL_LENGTH(N'dbo.[Order]', N'RefundAccountHolder') IS NULL
        ALTER TABLE dbo.[Order] ADD RefundAccountHolder nvarchar(150) NULL;

    IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.[Order]')
        AND system_type_id = 231 AND is_nullable = 1 AND is_computed = 0 AND default_object_id = 0
        AND ((name = N'RefundBankName' AND max_length = 200)
            OR (name = N'RefundAccountNumber' AND max_length = 68)
            OR (name = N'RefundAccountHolder' AND max_length = 300))) <> 3
        THROW 51000, 'Unexpected Ordering refund account column definitions.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
