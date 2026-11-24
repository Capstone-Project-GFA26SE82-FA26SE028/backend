SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE TABLE dbo.[Category]
(
    [CategoryId] uniqueidentifier NOT NULL,
    [CategoryCode] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [CategoryName] nvarchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [IsActive] bit NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[Item]
(
    [ItemId] uniqueidentifier NOT NULL,
    [CategoryId] uniqueidentifier NOT NULL,
    [ItemCode] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [ItemName] nvarchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [Description] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [ImageUrl] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [Availability] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [KitchenStationRef] uniqueidentifier NULL,
    [PreparationTimeMinutes] int NULL,
    [Version] timestamp NOT NULL,
    [RestaurantRef] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[ItemOptionGroup]
(
    [ItemId] uniqueidentifier NOT NULL,
    [OptionGroupId] uniqueidentifier NOT NULL,
    [MinSelection] int NOT NULL,
    [MaxSelection] int NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[Option]
(
    [OptionId] uniqueidentifier NOT NULL,
    [OptionGroupId] uniqueidentifier NOT NULL,
    [OptionName] nvarchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [ExtraPrice] decimal(18,2) NOT NULL,
    [Availability] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[OptionGroup]
(
    [OptionGroupId] uniqueidentifier NOT NULL,
    [OptionGroupName] nvarchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[Price]
(
    [PriceId] uniqueidentifier NOT NULL,
    [ItemId] uniqueidentifier NOT NULL,
    [Channel] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [EffectiveFrom] datetime NOT NULL,
    [EffectiveTo] datetime NULL,
    [DailyStart] time(0) NULL,
    [DailyEnd] time(0) NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[RecipeIngredient]
(
    [RecipeIngredientId] uniqueidentifier NOT NULL,
    [RecipeVersionId] uniqueidentifier NOT NULL,
    [IngredientRef] uniqueidentifier NOT NULL,
    [UnitRef] uniqueidentifier NOT NULL,
    [Quantity] decimal(18,6) NOT NULL,
    [BaseQuantitySnapshot] decimal(18,6) NOT NULL,
    [ConversionFactorSnapshot] decimal(18,6) NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[RecipeMedia]
(
    [RecipeMediaId] uniqueidentifier NOT NULL,
    [RecipeVersionId] uniqueidentifier NOT NULL,
    [MediaType] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [Url] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [DisplayOrder] int NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[RecipeVersion]
(
    [RecipeVersionId] uniqueidentifier NOT NULL,
    [ItemId] uniqueidentifier NULL,
    [OptionId] uniqueidentifier NULL,
    [Version] int NOT NULL,
    [EffectiveFrom] datetime NOT NULL,
    [EffectiveTo] datetime NULL,
    [Instructions] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE NONCLUSTERED INDEX [IX_Category_RestaurantIsolation] ON dbo.[Category] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[Category] ADD CONSTRAINT [PK_Category] PRIMARY KEY CLUSTERED ([CategoryId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_Item_RestaurantIsolation] ON dbo.[Item] ([RestaurantRef] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_Item_RestaurantRef] ON dbo.[Item] ([RestaurantRef] ASC);
GO
ALTER TABLE dbo.[Item] ADD CONSTRAINT [PK_Item] PRIMARY KEY CLUSTERED ([ItemId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_ItemOptionGroup_RestaurantIsolation] ON dbo.[ItemOptionGroup] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[ItemOptionGroup] ADD CONSTRAINT [PK_ItemOptionGroup] PRIMARY KEY CLUSTERED ([ItemId] ASC, [OptionGroupId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_Option_RestaurantIsolation] ON dbo.[Option] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[Option] ADD CONSTRAINT [PK_Option] PRIMARY KEY CLUSTERED ([OptionId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_OptionGroup_RestaurantIsolation] ON dbo.[OptionGroup] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[OptionGroup] ADD CONSTRAINT [PK_OptionGroup] PRIMARY KEY CLUSTERED ([OptionGroupId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_Price_RestaurantIsolation] ON dbo.[Price] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[Price] ADD CONSTRAINT [PK_Price] PRIMARY KEY CLUSTERED ([PriceId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_RecipeIngredient_RestaurantIsolation] ON dbo.[RecipeIngredient] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[RecipeIngredient] ADD CONSTRAINT [PK_RecipeIngredient] PRIMARY KEY CLUSTERED ([RecipeIngredientId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_RecipeMedia_RestaurantIsolation] ON dbo.[RecipeMedia] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[RecipeMedia] ADD CONSTRAINT [PK_RecipeMedia] PRIMARY KEY CLUSTERED ([RecipeMediaId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_RecipeVersion_RestaurantIsolation] ON dbo.[RecipeVersion] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[RecipeVersion] ADD CONSTRAINT [PK_RecipeVersion] PRIMARY KEY CLUSTERED ([RecipeVersionId] ASC);
GO

ALTER TABLE dbo.[Item] WITH CHECK ADD CONSTRAINT [FK_Item_Category] FOREIGN KEY ([CategoryId]) REFERENCES dbo.[Category] ([CategoryId]);
GO
ALTER TABLE dbo.[ItemOptionGroup] WITH CHECK ADD CONSTRAINT [FK_ItemOptionGroup_Item] FOREIGN KEY ([ItemId]) REFERENCES dbo.[Item] ([ItemId]);
GO
ALTER TABLE dbo.[ItemOptionGroup] WITH CHECK ADD CONSTRAINT [FK_ItemOptionGroup_OptionGroup] FOREIGN KEY ([OptionGroupId]) REFERENCES dbo.[OptionGroup] ([OptionGroupId]);
GO
ALTER TABLE dbo.[Option] WITH CHECK ADD CONSTRAINT [FK_Option_OptionGroup] FOREIGN KEY ([OptionGroupId]) REFERENCES dbo.[OptionGroup] ([OptionGroupId]);
GO
ALTER TABLE dbo.[Price] WITH CHECK ADD CONSTRAINT [FK_Price_Item] FOREIGN KEY ([ItemId]) REFERENCES dbo.[Item] ([ItemId]);
GO
ALTER TABLE dbo.[RecipeIngredient] WITH CHECK ADD CONSTRAINT [FK_RecipeIngredient_RecipeVersion] FOREIGN KEY ([RecipeVersionId]) REFERENCES dbo.[RecipeVersion] ([RecipeVersionId]);
GO
ALTER TABLE dbo.[RecipeMedia] WITH CHECK ADD CONSTRAINT [FK_RecipeMedia_RecipeVersion] FOREIGN KEY ([RecipeVersionId]) REFERENCES dbo.[RecipeVersion] ([RecipeVersionId]);
GO
ALTER TABLE dbo.[RecipeVersion] WITH CHECK ADD CONSTRAINT [FK_RecipeVersion_Item] FOREIGN KEY ([ItemId]) REFERENCES dbo.[Item] ([ItemId]);
GO
ALTER TABLE dbo.[RecipeVersion] WITH CHECK ADD CONSTRAINT [FK_RecipeVersion_Option] FOREIGN KEY ([OptionId]) REFERENCES dbo.[Option] ([OptionId]);
GO
