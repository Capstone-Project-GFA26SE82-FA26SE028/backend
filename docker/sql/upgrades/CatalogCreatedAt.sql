SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'dbo.Category', N'CreatedAt') IS NULL
BEGIN
    ALTER TABLE dbo.[Category]
        ADD [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_Category_CreatedAt] DEFAULT (SYSUTCDATETIME());
END;
GO

IF COL_LENGTH(N'dbo.Item', N'CreatedAt') IS NULL
BEGIN
    ALTER TABLE dbo.[Item]
        ADD [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_Item_CreatedAt] DEFAULT (SYSUTCDATETIME());
END;
GO

IF COL_LENGTH(N'dbo.Option', N'CreatedAt') IS NULL
BEGIN
    ALTER TABLE dbo.[Option]
        ADD [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_Option_CreatedAt] DEFAULT (SYSUTCDATETIME());
END;
GO

IF COL_LENGTH(N'dbo.OptionGroup', N'CreatedAt') IS NULL
BEGIN
    ALTER TABLE dbo.[OptionGroup]
        ADD [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_OptionGroup_CreatedAt] DEFAULT (SYSUTCDATETIME());
END;
GO
