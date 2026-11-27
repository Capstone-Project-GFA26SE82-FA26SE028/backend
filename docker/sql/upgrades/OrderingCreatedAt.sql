SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'dbo.KitchenAssignment', N'CreatedAt') IS NULL
BEGIN
    ALTER TABLE dbo.[KitchenAssignment]
        ADD [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_KitchenAssignment_CreatedAt] DEFAULT (SYSUTCDATETIME());
END;
GO

IF COL_LENGTH(N'dbo.KitchenStation', N'CreatedAt') IS NULL
BEGIN
    ALTER TABLE dbo.[KitchenStation]
        ADD [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_KitchenStation_CreatedAt] DEFAULT (SYSUTCDATETIME());
END;
GO
