-- Run against InventoryDb before starting the updated Inventory service.
-- Additive and repeatable. Does not recalculate stock or historical receipts.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @lockResult int;
EXEC @lockResult = sys.sp_getapplock @Resource = N'InventoryUnitsUpgrade',
    @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 30000;
IF @lockResult < 0 THROW 51000, 'Cannot acquire Inventory units upgrade lock.', 1;

IF OBJECT_ID(N'dbo.Unit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Unit (
        UnitId uniqueidentifier NOT NULL CONSTRAINT PK_Unit PRIMARY KEY,
        Code varchar(50) NOT NULL CONSTRAINT UQ_Unit_Code UNIQUE,
        Name nvarchar(100) NOT NULL
    );
END;
IF OBJECT_ID(N'dbo.IngredientUnit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.IngredientUnit (
        IngredientId uniqueidentifier NOT NULL,
        UnitId uniqueidentifier NOT NULL,
        ConversionFactor decimal(18,6) NOT NULL,
        CONSTRAINT PK_IngredientUnit PRIMARY KEY (IngredientId, UnitId),
        CONSTRAINT FK_IngredientUnit_Ingredient FOREIGN KEY (IngredientId) REFERENCES dbo.Ingredient(IngredientId),
        CONSTRAINT FK_IngredientUnit_Unit FOREIGN KEY (UnitId) REFERENCES dbo.Unit(UnitId),
        CONSTRAINT CK_IngredientUnit_Factor CHECK (ConversionFactor > 0)
    );
END;
IF COL_LENGTH('dbo.GoodsReceiptItem', 'InputUnitId') IS NULL
    ALTER TABLE dbo.GoodsReceiptItem ADD InputUnitId uniqueidentifier NULL
        CONSTRAINT FK_GoodsReceiptItem_Unit REFERENCES dbo.Unit(UnitId);
IF COL_LENGTH('dbo.GoodsReceiptItem', 'InputQuantity') IS NULL
    ALTER TABLE dbo.GoodsReceiptItem ADD InputQuantity decimal(18,6) NULL;
IF COL_LENGTH('dbo.GoodsReceiptItem', 'ConversionFactorSnapshot') IS NULL
    ALTER TABLE dbo.GoodsReceiptItem ADD ConversionFactorSnapshot decimal(18,6) NULL;

-- The gram ID matches the existing demo Catalog UnitRef. Never reassign existing IDs.
IF EXISTS (SELECT 1 FROM dbo.Unit WHERE Code = 'g' AND UnitId <> '20000000-0000-0000-0000-000000000100')
    THROW 51001, 'Existing gram unit ID differs from demo Catalog UnitRef. Reconcile explicitly.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.Unit WHERE Code = 'g')
    INSERT dbo.Unit VALUES ('20000000-0000-0000-0000-000000000100', 'g', N'Gram');
IF NOT EXISTS (SELECT 1 FROM dbo.Unit WHERE Code = 'kg')
    INSERT dbo.Unit VALUES ('20000000-0000-0000-0000-000000000101', 'kg', N'Kilogram');
INSERT dbo.Unit (UnitId, Code, Name)
SELECT NEWID(), source.Code, source.Code
FROM (SELECT DISTINCT LOWER(LTRIM(RTRIM(BaseUnit))) AS Code FROM dbo.Ingredient) source
WHERE NOT EXISTS (SELECT 1 FROM dbo.Unit u WHERE u.Code = source.Code);
COMMIT TRANSACTION;
