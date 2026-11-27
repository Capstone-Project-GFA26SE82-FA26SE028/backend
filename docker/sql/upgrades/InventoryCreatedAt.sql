SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'dbo.Ingredient', N'CreatedAt') IS NULL
BEGIN
    ALTER TABLE dbo.[Ingredient]
        ADD [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_Ingredient_CreatedAt] DEFAULT (SYSUTCDATETIME());
END;
GO

IF COL_LENGTH(N'dbo.Unit', N'CreatedAt') IS NULL
BEGIN
    ALTER TABLE dbo.[Unit]
        ADD [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_Unit_CreatedAt] DEFAULT (SYSUTCDATETIME());
END;
GO
