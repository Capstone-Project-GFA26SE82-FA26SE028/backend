SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'dbo.Bill', N'CreatedAt') IS NULL
BEGIN
    ALTER TABLE dbo.[Bill]
        ADD [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_Bill_CreatedAt] DEFAULT (SYSUTCDATETIME());
END;
GO

IF COL_LENGTH(N'dbo.CashRegister', N'CreatedAt') IS NULL
BEGIN
    ALTER TABLE dbo.[CashRegister]
        ADD [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_CashRegister_CreatedAt] DEFAULT (SYSUTCDATETIME());
END;
GO
