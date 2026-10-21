SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[GoodsReceipt](
	[GoodsReceiptId] [uniqueidentifier] NOT NULL,
	[ReceiptCode] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Status] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[CreatedByRef] [uniqueidentifier] NOT NULL,
	[CreatedAt] [datetime] NOT NULL,
	[ConfirmAt] [datetime] NULL,
	[Note] [varchar](max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
 CONSTRAINT [PK_GoodsReceipt] PRIMARY KEY CLUSTERED 
(
	[GoodsReceiptId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Stocktake](
	[StocktakeId] [uniqueidentifier] NOT NULL,
	[StocktakeCode] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Status] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[CreatedByRef] [uniqueidentifier] NOT NULL,
	[ApprovedByRef] [uniqueidentifier] NULL,
	[CreatedAt] [datetime] NOT NULL,
	[ConfirmedAt] [datetime] NULL,
 CONSTRAINT [PK_Stocktake] PRIMARY KEY CLUSTERED 
(
	[StocktakeId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Ingredient](
	[IngredientId] [uniqueidentifier] NOT NULL,
	[IngredientCode] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[IngredientName] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[BaseUnit] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[StockQuantity] [decimal](18, 6) NOT NULL,
	[LowStockThreshold] [decimal](18, 6) NOT NULL,
	[StockVersion] [timestamp] NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_Ingredient] PRIMARY KEY CLUSTERED 
(
	[IngredientId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[StocktakeItem](
	[StocktakeItemId] [uniqueidentifier] NOT NULL,
	[StocktakeId] [uniqueidentifier] NOT NULL,
	[IngredientId] [uniqueidentifier] NOT NULL,
	[SystemQuantity] [decimal](18, 6) NOT NULL,
	[ActualQuantity] [decimal](18, 6) NULL,
	[SnapshotVersion] [binary](8) NOT NULL,
	[DiscrepancyReason] [varchar](max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
 CONSTRAINT [PK_StocktakeItem] PRIMARY KEY CLUSTERED 
(
	[StocktakeItemId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[GoodsReceiptItem](
	[GoodsReceiptItemId] [uniqueidentifier] NOT NULL,
	[GoodsReceiptId] [uniqueidentifier] NOT NULL,
	[IngredientId] [uniqueidentifier] NOT NULL,
	[ReceivedQuantity] [decimal](18, 6) NOT NULL,
	[ExpiryDate] [date] NULL,
 CONSTRAINT [PK_GoodsReceiptItem] PRIMARY KEY CLUSTERED 
(
	[GoodsReceiptItemId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[StockMovement](
	[StockMovementId] [uniqueidentifier] NOT NULL,
	[IngredientId] [uniqueidentifier] NOT NULL,
	[GoodsReceiptItemId] [uniqueidentifier] NULL,
	[StocktakeItemId] [uniqueidentifier] NULL,
	[MovementType] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[QuantityChange] [decimal](18, 6) NOT NULL,
	[CookingEventRef] [uniqueidentifier] NULL,
	[OrderItemRef] [uniqueidentifier] NULL,
	[PerformedByRef] [uniqueidentifier] NULL,
	[Reason] [varchar](max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[CreatedAt] [datetime] NOT NULL,
 CONSTRAINT [PK_StockMovement] PRIMARY KEY CLUSTERED 
(
	[StockMovementId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
ALTER TABLE [dbo].[StocktakeItem]  WITH CHECK ADD  CONSTRAINT [FK_StocktakeItem_Ingredient] FOREIGN KEY([IngredientId])
REFERENCES [dbo].[Ingredient] ([IngredientId])
GO
ALTER TABLE [dbo].[StocktakeItem] CHECK CONSTRAINT [FK_StocktakeItem_Ingredient]
GO
ALTER TABLE [dbo].[StocktakeItem]  WITH CHECK ADD  CONSTRAINT [FK_StocktakeItem_Stocktake] FOREIGN KEY([StocktakeId])
REFERENCES [dbo].[Stocktake] ([StocktakeId])
GO
ALTER TABLE [dbo].[StocktakeItem] CHECK CONSTRAINT [FK_StocktakeItem_Stocktake]
GO
ALTER TABLE [dbo].[GoodsReceiptItem]  WITH CHECK ADD  CONSTRAINT [FK_GoodsReceiptItem_GoodsReceipt] FOREIGN KEY([GoodsReceiptId])
REFERENCES [dbo].[GoodsReceipt] ([GoodsReceiptId])
GO
ALTER TABLE [dbo].[GoodsReceiptItem] CHECK CONSTRAINT [FK_GoodsReceiptItem_GoodsReceipt]
GO
ALTER TABLE [dbo].[GoodsReceiptItem]  WITH CHECK ADD  CONSTRAINT [FK_GoodsReceiptItem_Ingredient] FOREIGN KEY([IngredientId])
REFERENCES [dbo].[Ingredient] ([IngredientId])
GO
ALTER TABLE [dbo].[GoodsReceiptItem] CHECK CONSTRAINT [FK_GoodsReceiptItem_Ingredient]
GO
ALTER TABLE [dbo].[StockMovement]  WITH CHECK ADD  CONSTRAINT [FK_StockMovement_GoodsReceiptItem] FOREIGN KEY([GoodsReceiptItemId])
REFERENCES [dbo].[GoodsReceiptItem] ([GoodsReceiptItemId])
GO
ALTER TABLE [dbo].[StockMovement] CHECK CONSTRAINT [FK_StockMovement_GoodsReceiptItem]
GO
ALTER TABLE [dbo].[StockMovement]  WITH CHECK ADD  CONSTRAINT [FK_StockMovement_Ingredient] FOREIGN KEY([IngredientId])
REFERENCES [dbo].[Ingredient] ([IngredientId])
GO
ALTER TABLE [dbo].[StockMovement] CHECK CONSTRAINT [FK_StockMovement_Ingredient]
GO
ALTER TABLE [dbo].[StockMovement]  WITH CHECK ADD  CONSTRAINT [FK_StockMovement_StocktakeItem] FOREIGN KEY([StocktakeItemId])
REFERENCES [dbo].[StocktakeItem] ([StocktakeItemId])
GO
ALTER TABLE [dbo].[StockMovement] CHECK CONSTRAINT [FK_StockMovement_StocktakeItem]
GO
