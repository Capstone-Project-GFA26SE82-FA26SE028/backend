SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @LockResult int;
    EXEC @LockResult = sys.sp_getapplock @Resource = N'NotificationOutbox.Schema',
        @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 30000;
    IF @LockResult < 0
        THROW 51000, 'Cannot acquire the notification outbox upgrade lock.', 1;

    IF OBJECT_ID(N'dbo.NotificationOutbox', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.NotificationOutbox
        (
            NotificationOutboxId uniqueidentifier NOT NULL CONSTRAINT PK_NotificationOutbox PRIMARY KEY,
            RestaurantId uniqueidentifier NOT NULL,
            EventKey nvarchar(200) NOT NULL,
            Payload nvarchar(max) NOT NULL,
            CreatedAt datetime2(7) NOT NULL,
            NextAttemptAt datetime2(7) NOT NULL,
            ProcessedAt datetime2(7) NULL,
            Attempts int NOT NULL,
            LastError nvarchar(100) NULL
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.NotificationOutbox')
        AND name = N'IX_NotificationOutbox_EventKey')
        CREATE UNIQUE INDEX IX_NotificationOutbox_EventKey ON dbo.NotificationOutbox(EventKey);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.NotificationOutbox')
        AND name = N'IX_NotificationOutbox_ProcessedAt_NextAttemptAt')
        CREATE INDEX IX_NotificationOutbox_ProcessedAt_NextAttemptAt ON dbo.NotificationOutbox(ProcessedAt, NextAttemptAt);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
