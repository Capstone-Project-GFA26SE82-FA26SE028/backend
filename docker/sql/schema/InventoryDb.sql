SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE TABLE dbo.[GoodsReceipt]
(
    [GoodsReceiptId] uniqueidentifier NOT NULL,
    [ReceiptCode] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [Status] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [CreatedByRef] uniqueidentifier NOT NULL,
    [CreatedAt] datetime NOT NULL,
    [ConfirmAt] datetime NULL,
    [Note] varchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[GoodsReceiptItem]
(
    [GoodsReceiptItemId] uniqueidentifier NOT NULL,
    [GoodsReceiptId] uniqueidentifier NOT NULL,
    [IngredientId] uniqueidentifier NOT NULL,
    [ReceivedQuantity] decimal(18,6) NOT NULL,
    [ExpiryDate] date NULL,
    [InputUnitId] uniqueidentifier NULL,
    [InputQuantity] decimal(18,6) NULL,
    [ConversionFactorSnapshot] decimal(18,6) NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[Ingredient]
(
    [IngredientId] uniqueidentifier NOT NULL,
    [IngredientCode] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [IngredientName] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [BaseUnit] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [StockQuantity] decimal(18,6) NOT NULL,
    [LowStockThreshold] decimal(18,6) NOT NULL,
    [StockVersion] timestamp NOT NULL,
    [IsActive] bit NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[IngredientUnit]
(
    [IngredientId] uniqueidentifier NOT NULL,
    [UnitId] uniqueidentifier NOT NULL,
    [ConversionFactor] decimal(18,6) NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[StockIssue]
(
    [StockIssueId] uniqueidentifier NOT NULL,
    [CreatedByRef] uniqueidentifier NOT NULL,
    [CreatedAt] datetime2(7) NOT NULL,
    [Reason] nvarchar(1000) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [RequestHash] varchar(64) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[StockMovement]
(
    [StockMovementId] uniqueidentifier NOT NULL,
    [IngredientId] uniqueidentifier NOT NULL,
    [GoodsReceiptItemId] uniqueidentifier NULL,
    [StocktakeItemId] uniqueidentifier NULL,
    [MovementType] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [QuantityChange] decimal(18,6) NOT NULL,
    [CookingEventRef] uniqueidentifier NULL,
    [OrderItemRef] uniqueidentifier NULL,
    [PerformedByRef] uniqueidentifier NULL,
    [Reason] varchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [CreatedAt] datetime NOT NULL,
    [StockIssueId] uniqueidentifier NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[Stocktake]
(
    [StocktakeId] uniqueidentifier NOT NULL,
    [StocktakeCode] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [Status] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [CreatedByRef] uniqueidentifier NOT NULL,
    [ApprovedByRef] uniqueidentifier NULL,
    [CreatedAt] datetime NOT NULL,
    [ConfirmedAt] datetime NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[StocktakeItem]
(
    [StocktakeItemId] uniqueidentifier NOT NULL,
    [StocktakeId] uniqueidentifier NOT NULL,
    [IngredientId] uniqueidentifier NOT NULL,
    [SystemQuantity] decimal(18,6) NOT NULL,
    [ActualQuantity] decimal(18,6) NULL,
    [SnapshotVersion] binary(8) NOT NULL,
    [DiscrepancyReason] varchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[Unit]
(
    [UnitId] uniqueidentifier NOT NULL,
    [Code] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [Name] nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE NONCLUSTERED INDEX [IX_GoodsReceipt_RestaurantIsolation] ON dbo.[GoodsReceipt] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[GoodsReceipt] ADD CONSTRAINT [PK_GoodsReceipt] PRIMARY KEY CLUSTERED ([GoodsReceiptId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_GoodsReceiptItem_RestaurantIsolation] ON dbo.[GoodsReceiptItem] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[GoodsReceiptItem] ADD CONSTRAINT [PK_GoodsReceiptItem] PRIMARY KEY CLUSTERED ([GoodsReceiptItemId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_Ingredient_RestaurantIsolation] ON dbo.[Ingredient] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[Ingredient] ADD CONSTRAINT [PK_Ingredient] PRIMARY KEY CLUSTERED ([IngredientId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_IngredientUnit_RestaurantIsolation] ON dbo.[IngredientUnit] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[IngredientUnit] ADD CONSTRAINT [PK_IngredientUnit] PRIMARY KEY CLUSTERED ([IngredientId] ASC, [UnitId] ASC);
GO
ALTER TABLE dbo.[IngredientUnit] WITH CHECK ADD CONSTRAINT [CK_IngredientUnit_Factor] CHECK ([ConversionFactor]>(0));
GO

CREATE NONCLUSTERED INDEX [IX_StockIssue_RestaurantIsolation] ON dbo.[StockIssue] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[StockIssue] ADD CONSTRAINT [PK_StockIssue] PRIMARY KEY CLUSTERED ([StockIssueId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_StockMovement_GoodsReceiptItemId] ON dbo.[StockMovement] ([GoodsReceiptItemId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_StockMovement_RestaurantIsolation] ON dbo.[StockMovement] ([RestaurantId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_StockMovement_StockIssueId] ON dbo.[StockMovement] ([StockIssueId] ASC);
GO
ALTER TABLE dbo.[StockMovement] ADD CONSTRAINT [PK_StockMovement] PRIMARY KEY CLUSTERED ([StockMovementId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_Stocktake_RestaurantIsolation] ON dbo.[Stocktake] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[Stocktake] ADD CONSTRAINT [PK_Stocktake] PRIMARY KEY CLUSTERED ([StocktakeId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_StocktakeItem_RestaurantIsolation] ON dbo.[StocktakeItem] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[StocktakeItem] ADD CONSTRAINT [PK_StocktakeItem] PRIMARY KEY CLUSTERED ([StocktakeItemId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_Unit_RestaurantIsolation] ON dbo.[Unit] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[Unit] ADD CONSTRAINT [PK_Unit] PRIMARY KEY CLUSTERED ([UnitId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [UQ_Unit_Code] ON dbo.[Unit] ([RestaurantId] ASC, [Code] ASC);
GO

ALTER TABLE dbo.[GoodsReceiptItem] WITH CHECK ADD CONSTRAINT [FK_GoodsReceiptItem_GoodsReceipt] FOREIGN KEY ([GoodsReceiptId]) REFERENCES dbo.[GoodsReceipt] ([GoodsReceiptId]);
GO
ALTER TABLE dbo.[GoodsReceiptItem] WITH CHECK ADD CONSTRAINT [FK_GoodsReceiptItem_Ingredient] FOREIGN KEY ([IngredientId]) REFERENCES dbo.[Ingredient] ([IngredientId]);
GO
ALTER TABLE dbo.[GoodsReceiptItem] WITH CHECK ADD CONSTRAINT [FK_GoodsReceiptItem_Unit] FOREIGN KEY ([InputUnitId]) REFERENCES dbo.[Unit] ([UnitId]);
GO
ALTER TABLE dbo.[IngredientUnit] WITH CHECK ADD CONSTRAINT [FK_IngredientUnit_Ingredient] FOREIGN KEY ([IngredientId]) REFERENCES dbo.[Ingredient] ([IngredientId]);
GO
ALTER TABLE dbo.[IngredientUnit] WITH CHECK ADD CONSTRAINT [FK_IngredientUnit_Unit] FOREIGN KEY ([UnitId]) REFERENCES dbo.[Unit] ([UnitId]);
GO
ALTER TABLE dbo.[StockMovement] WITH CHECK ADD CONSTRAINT [FK_StockMovement_GoodsReceiptItem] FOREIGN KEY ([GoodsReceiptItemId]) REFERENCES dbo.[GoodsReceiptItem] ([GoodsReceiptItemId]);
GO
ALTER TABLE dbo.[StockMovement] WITH CHECK ADD CONSTRAINT [FK_StockMovement_Ingredient] FOREIGN KEY ([IngredientId]) REFERENCES dbo.[Ingredient] ([IngredientId]);
GO
ALTER TABLE dbo.[StockMovement] WITH CHECK ADD CONSTRAINT [FK_StockMovement_StockIssue] FOREIGN KEY ([StockIssueId]) REFERENCES dbo.[StockIssue] ([StockIssueId]);
GO
ALTER TABLE dbo.[StockMovement] WITH CHECK ADD CONSTRAINT [FK_StockMovement_StocktakeItem] FOREIGN KEY ([StocktakeItemId]) REFERENCES dbo.[StocktakeItem] ([StocktakeItemId]);
GO
ALTER TABLE dbo.[StocktakeItem] WITH CHECK ADD CONSTRAINT [FK_StocktakeItem_Ingredient] FOREIGN KEY ([IngredientId]) REFERENCES dbo.[Ingredient] ([IngredientId]);
GO
ALTER TABLE dbo.[StocktakeItem] WITH CHECK ADD CONSTRAINT [FK_StocktakeItem_Stocktake] FOREIGN KEY ([StocktakeId]) REFERENCES dbo.[Stocktake] ([StocktakeId]);
GO
