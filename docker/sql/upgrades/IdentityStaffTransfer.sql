SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @LockResult int;
    EXEC @LockResult = sys.sp_getapplock @Resource = N'Identity.StaffTransfer.Schema',
        @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 30000;
    IF @LockResult < 0
        THROW 51000, 'Cannot acquire the Identity StaffTransfer upgrade lock.', 1;
    IF OBJECT_ID(N'dbo.[User]', N'U') IS NULL
        THROW 51000, 'User is missing. Review the IdentityDb baseline before upgrading.', 1;

    IF OBJECT_ID(N'dbo.StaffTransfer', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.StaffTransfer (
            StaffTransferId uniqueidentifier NOT NULL CONSTRAINT PK_StaffTransfer PRIMARY KEY,
            UserId uniqueidentifier NOT NULL,
            SourceRestaurantId uniqueidentifier NOT NULL,
            TargetRestaurantId uniqueidentifier NOT NULL,
            RequestedBy uniqueidentifier NOT NULL,
            WasActive bit NOT NULL,
            CompletedAt datetime2 NULL,
            Status nvarchar(20) NOT NULL,
            Message nvarchar(500) NULL,
            CreatedAt datetime2 NOT NULL
        );
    END;

    IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.StaffTransfer')
        AND is_computed = 0 AND default_object_id = 0
        AND ((name IN (N'StaffTransferId', N'UserId', N'SourceRestaurantId', N'TargetRestaurantId', N'RequestedBy')
                AND system_type_id = 36 AND is_nullable = 0)
            OR (name = N'WasActive' AND system_type_id = 104 AND is_nullable = 0)
            OR (name = N'CompletedAt' AND system_type_id = 42 AND scale = 7 AND is_nullable = 1)
            OR (name = N'CreatedAt' AND system_type_id = 42 AND scale = 7 AND is_nullable = 0)
            OR (name = N'Status' AND system_type_id = 231 AND max_length = 40 AND is_nullable = 0)
            OR (name = N'Message' AND system_type_id = 231 AND max_length = 1000 AND is_nullable = 1))) <> 10
        THROW 51000, 'Unexpected StaffTransfer columns. Existing data was not rewritten; review the schema.', 1;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes i
        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = OBJECT_ID(N'dbo.StaffTransfer') AND i.is_primary_key = 1
            AND c.name = N'StaffTransferId' AND ic.key_ordinal = 1
            AND (SELECT COUNT(*) FROM sys.index_columns k WHERE k.object_id = i.object_id
                AND k.index_id = i.index_id AND k.key_ordinal > 0) = 1)
        THROW 51000, 'Unexpected StaffTransfer primary key. Review the schema before upgrading.', 1;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StaffTransfer')
        AND name = N'IX_StaffTransfer_UserId_Status')
        CREATE INDEX IX_StaffTransfer_UserId_Status ON dbo.StaffTransfer(UserId, Status);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes i
        WHERE i.object_id = OBJECT_ID(N'dbo.StaffTransfer') AND i.name = N'IX_StaffTransfer_UserId_Status'
            AND i.is_unique = 0 AND i.has_filter = 0 AND i.is_disabled = 0
            AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id
                AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 2
            AND EXISTS (SELECT 1 FROM sys.index_columns ic
                JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND c.name = N'UserId')
            AND EXISTS (SELECT 1 FROM sys.index_columns ic
                JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 2 AND c.name = N'Status'))
        THROW 51000, 'Unexpected StaffTransfer lookup index. Review the schema before upgrading.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
