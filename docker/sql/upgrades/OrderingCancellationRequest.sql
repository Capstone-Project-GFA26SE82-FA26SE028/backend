SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.CancellationRequest', N'Source') IS NULL
BEGIN
    ALTER TABLE dbo.CancellationRequest ADD [Source] varchar(20) NOT NULL
        CONSTRAINT DF_CancellationRequest_Source DEFAULT ('Customer');
END;
IF COL_LENGTH(N'dbo.CancellationRequest', N'DecidedAt') IS NULL
BEGIN
    ALTER TABLE dbo.CancellationRequest ADD DecidedAt datetime NULL;
END;
IF COL_LENGTH(N'dbo.CancellationRequest', N'DecisionReason') IS NULL
BEGIN
    ALTER TABLE dbo.CancellationRequest ADD DecisionReason nvarchar(1000) NULL;
END;

-- Older volumes used varchar(max). Keep any historical reasons longer than the current validation limit.
IF EXISTS (SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.CancellationRequest') AND name = N'Reason' AND system_type_id = TYPE_ID(N'varchar'))
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.CancellationRequest WHERE DATALENGTH(CONVERT(nvarchar(max), Reason)) > 2000)
        ALTER TABLE dbo.CancellationRequest ALTER COLUMN Reason nvarchar(max) NOT NULL;
    ELSE
        ALTER TABLE dbo.CancellationRequest ALTER COLUMN Reason nvarchar(1000) NOT NULL;
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.CancellationRequest') AND name = N'UX_CancellationRequest_PendingItem')
BEGIN
    IF EXISTS (SELECT OrderItemId FROM dbo.CancellationRequest WHERE [Status] = 'Pending'
        GROUP BY OrderItemId HAVING COUNT(*) > 1)
        THROW 51000, 'CancellationRequest contains duplicate pending items. Resolve them before creating the unique index.', 1;

    CREATE UNIQUE NONCLUSTERED INDEX UX_CancellationRequest_PendingItem
        ON dbo.CancellationRequest (OrderItemId) WHERE [Status] = 'Pending';
END;

COMMIT TRANSACTION;
GO
