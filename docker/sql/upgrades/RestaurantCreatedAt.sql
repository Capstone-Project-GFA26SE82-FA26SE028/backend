SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'dbo.Restaurant', N'CreatedAt') IS NULL
BEGIN
    ALTER TABLE dbo.[Restaurant]
        ADD [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_Restaurant_CreatedAt] DEFAULT (SYSUTCDATETIME());
END;
GO

IF COL_LENGTH(N'dbo.Area', N'CreatedAt') IS NULL
BEGIN
    ALTER TABLE dbo.[Area]
        ADD [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_Area_CreatedAt] DEFAULT (SYSUTCDATETIME());
END;
GO

IF COL_LENGTH(N'dbo.Table', N'CreatedAt') IS NULL
BEGIN
    ALTER TABLE dbo.[Table]
        ADD [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_Table_CreatedAt] DEFAULT (SYSUTCDATETIME());
END;
GO
