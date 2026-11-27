SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.TableTransferSession', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[TableTransferSession] (
        [OperationId] uniqueidentifier NOT NULL PRIMARY KEY,
        [SourceSessionId] uniqueidentifier NOT NULL,
        [TargetSessionId] uniqueidentifier NOT NULL,
        [TargetTableId] uniqueidentifier NOT NULL,
        [ActorId] uniqueidentifier NOT NULL,
        [RestaurantId] uniqueidentifier NOT NULL,
        [RequestHash] nvarchar(64) NOT NULL,
        [Mode] nvarchar(10) NOT NULL,
        [Reason] nvarchar(1000) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [CreatedAt] datetime2 NOT NULL, [TargetCreated] bit NOT NULL
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.TableTransferSession') AND name = N'IX_TableTransferSession_Source')
BEGIN
    CREATE INDEX IX_TableTransferSession_Source ON [dbo].[TableTransferSession] ([SourceSessionId], [Status]);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.TableTransferSession') AND name = N'IX_TableTransferSession_Target')
BEGIN
    CREATE INDEX IX_TableTransferSession_Target ON [dbo].[TableTransferSession] ([TargetSessionId], [Status]);
END;

COMMIT TRANSACTION;
GO
