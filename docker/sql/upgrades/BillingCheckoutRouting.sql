SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.BillCheckout', N'U') IS NULL OR OBJECT_ID(N'dbo.CashRegister', N'U') IS NULL
    THROW 51000, 'Billing checkout and cash register tables must exist before routing upgrade.', 1;

IF COL_LENGTH(N'dbo.BillCheckout', N'CashRegisterId') IS NULL
    ALTER TABLE dbo.BillCheckout ADD CashRegisterId uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.BillCheckout', N'SubmittedAt') IS NULL
    ALTER TABLE dbo.BillCheckout ADD SubmittedAt datetime2(7) NULL;
IF COL_LENGTH(N'dbo.BillCheckout', N'PreparedByRef') IS NULL
    ALTER TABLE dbo.BillCheckout ADD PreparedByRef uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.BillCheckout', N'PreparedAt') IS NULL
    ALTER TABLE dbo.BillCheckout ADD PreparedAt datetime2(7) NULL;
IF COL_LENGTH(N'dbo.BillCheckout', N'PresentedAt') IS NULL
    ALTER TABLE dbo.BillCheckout ADD PresentedAt datetime2(7) NULL;

IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.BillCheckout')) <> 12
    THROW 51000, 'BillCheckout has an unexpected schema. Review it before upgrading.', 1;

IF EXISTS
(
    SELECT 1 FROM (VALUES
        (N'CashRegisterId', N'uniqueidentifier', 16, 0),
        (N'SubmittedAt', N'datetime2', 8, 7),
        (N'PreparedByRef', N'uniqueidentifier', 16, 0),
        (N'PreparedAt', N'datetime2', 8, 7),
        (N'PresentedAt', N'datetime2', 8, 7)
    ) expected(Name, TypeName, Length, Scale)
    LEFT JOIN sys.columns c ON c.object_id = OBJECT_ID(N'dbo.BillCheckout') AND c.name = expected.Name
    LEFT JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.column_id IS NULL OR t.name <> expected.TypeName OR c.max_length <> expected.Length
        OR c.scale <> expected.Scale OR c.is_nullable <> 1 OR c.default_object_id <> 0
)
    THROW 51000, 'Checkout routing columns differ from the approved nullable schema.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.BillCheckout')
    AND name = N'FK_BillCheckout_CashRegister')
BEGIN
    ALTER TABLE dbo.BillCheckout WITH CHECK ADD CONSTRAINT FK_BillCheckout_CashRegister
        FOREIGN KEY (CashRegisterId) REFERENCES dbo.CashRegister(CashRegisterId);
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys fk
    JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
    JOIN sys.columns parent ON parent.object_id = fkc.parent_object_id AND parent.column_id = fkc.parent_column_id
    JOIN sys.columns target ON target.object_id = fkc.referenced_object_id AND target.column_id = fkc.referenced_column_id
    WHERE fk.parent_object_id = OBJECT_ID(N'dbo.BillCheckout') AND fk.name = N'FK_BillCheckout_CashRegister'
        AND fk.referenced_object_id = OBJECT_ID(N'dbo.CashRegister') AND parent.name = N'CashRegisterId'
        AND target.name = N'CashRegisterId' AND fk.is_disabled = 0 AND fk.is_not_trusted = 0
        AND fk.delete_referential_action = 0
)
    THROW 51000, 'Checkout routing requires its trusted local cash register foreign key.', 1;

COMMIT TRANSACTION;
GO
