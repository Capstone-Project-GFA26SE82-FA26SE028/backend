SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.CheckoutLock', N'U') IS NULL
BEGIN
CREATE TABLE dbo.CheckoutLock
(
    CheckoutId uniqueidentifier NOT NULL,
    BillIdRef uniqueidentifier NULL,
    TableSessionRef uniqueidentifier NOT NULL,
    SourceHash char(64) NULL,
    Status varchar(20) NOT NULL,
    CreatedAt datetime2(7) NOT NULL,
    ReleasedAt datetime2(7) NULL,
    CONSTRAINT PK_CheckoutLock PRIMARY KEY (CheckoutId)
);
CREATE UNIQUE INDEX UX_CheckoutLock_ActiveSession ON dbo.CheckoutLock(TableSessionRef) WHERE ReleasedAt IS NULL;
END;
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CheckoutLock')) <> 7
    THROW 51000, 'CheckoutLock has an unexpected schema. Review it before upgrading.', 1;
IF EXISTS
(
    SELECT 1 FROM (VALUES
(N'CheckoutId',N'uniqueidentifier',16,0),
(N'BillIdRef',N'uniqueidentifier',16,1),
(N'TableSessionRef',N'uniqueidentifier',16,0),
(N'SourceHash',N'char',64,1),
(N'Status',N'varchar',20,0),
(N'CreatedAt',N'datetime2',8,0),
(N'ReleasedAt',N'datetime2',8,1)
    ) AS expected(Name,TypeName,Length,Nullable)
    LEFT JOIN sys.columns AS c ON c.object_id=OBJECT_ID(N'dbo.CheckoutLock') AND c.name=expected.Name
    LEFT JOIN sys.types AS t ON c.user_type_id=t.user_type_id
    WHERE c.column_id IS NULL OR t.name<>expected.TypeName OR c.max_length<>expected.Length
        OR c.is_nullable<>expected.Nullable OR c.default_object_id<>0
        OR (expected.TypeName='datetime2' AND c.scale<>7)
)
    THROW 51000, 'CheckoutLock columns differ from the approved checkout schema.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.CheckoutLock')
    AND name=N'UX_CheckoutLock_ActiveSession' AND is_unique=1 AND has_filter=1)
    THROW 51000, 'CheckoutLock requires the active-session unique filtered index.', 1;
IF NOT EXISTS
(
    SELECT 1 FROM sys.key_constraints k
    JOIN sys.index_columns ic ON ic.object_id=k.parent_object_id AND ic.index_id=k.unique_index_id AND ic.key_ordinal=1
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE k.parent_object_id=OBJECT_ID(N'dbo.CheckoutLock') AND k.type='PK' AND c.name=N'CheckoutId'
)
    THROW 51000, 'CheckoutLock requires its CheckoutId primary key.', 1;
IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes i
    JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=1
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'dbo.CheckoutLock') AND i.name=N'UX_CheckoutLock_ActiveSession'
        AND c.name=N'TableSessionRef'
        AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(i.filter_definition,'[',''),']',''),' ',''),'(',''),')','')=N'ReleasedAtISNULL'
)
    THROW 51000, 'CheckoutLock active-session index differs from the approved filter or key.', 1;
COMMIT TRANSACTION;
GO
