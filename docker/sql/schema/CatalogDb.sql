SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Category](
	[CategoryId] [uniqueidentifier] NOT NULL,
	[CategoryCode] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[CategoryName] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_Category] PRIMARY KEY CLUSTERED 
(
	[CategoryId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[OptionGroup](
	[OptionGroupId] [uniqueidentifier] NOT NULL,
	[OptionGroupName] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
 CONSTRAINT [PK_OptionGroup] PRIMARY KEY CLUSTERED 
(
	[OptionGroupId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Item](
	[ItemId] [uniqueidentifier] NOT NULL,
	[CategoryId] [uniqueidentifier] NOT NULL,
	[ItemCode] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[ItemName] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Description] [varchar](max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[ImageUrl] [varchar](max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[Availability] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[KitchenStationRef] [uniqueidentifier] NULL,
	[Version] [timestamp] NOT NULL,
 CONSTRAINT [PK_Item] PRIMARY KEY CLUSTERED 
(
	[ItemId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Option](
	[OptionId] [uniqueidentifier] NOT NULL,
	[OptionGroupId] [uniqueidentifier] NOT NULL,
	[OptionName] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[ExtraPrice] [decimal](18, 2) NOT NULL,
	[Availability] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
 CONSTRAINT [PK_Option] PRIMARY KEY CLUSTERED 
(
	[OptionId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ItemOptionGroup](
	[ItemId] [uniqueidentifier] NOT NULL,
	[OptionGroupId] [uniqueidentifier] NOT NULL,
	[MinSelection] [int] NOT NULL,
	[MaxSelection] [int] NOT NULL,
 CONSTRAINT [PK_ItemOptionGroup] PRIMARY KEY CLUSTERED 
(
	[ItemId] ASC,
	[OptionGroupId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Price](
	[PriceId] [uniqueidentifier] NOT NULL,
	[ItemId] [uniqueidentifier] NOT NULL,
	[Channel] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Amount] [decimal](18, 2) NOT NULL,
	[EffectiveFrom] [datetime] NOT NULL,
	[EffectiveTo] [datetime] NULL,
	[DailyStart] [time](0) NULL,
	[DailyEnd] [time](0) NULL,
 CONSTRAINT [PK_Price] PRIMARY KEY CLUSTERED 
(
	[PriceId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[RecipeVersion](
	[RecipeVersionId] [uniqueidentifier] NOT NULL,
	[ItemId] [uniqueidentifier] NULL,
	[OptionId] [uniqueidentifier] NULL,
	[Version] [int] NOT NULL,
	[EffectiveFrom] [datetime] NOT NULL,
	[EffectiveTo] [datetime] NULL,
	[Instructions] [varchar](max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
 CONSTRAINT [PK_RecipeVersion] PRIMARY KEY CLUSTERED 
(
	[RecipeVersionId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[RecipeMedia](
	[RecipeMediaId] [uniqueidentifier] NOT NULL,
	[RecipeVersionId] [uniqueidentifier] NOT NULL,
	[MediaType] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Url] [varchar](max) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[DisplayOrder] [int] NOT NULL,
 CONSTRAINT [PK_RecipeMedia] PRIMARY KEY CLUSTERED 
(
	[RecipeMediaId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[RecipeIngredient](
	[RecipeIngredientId] [uniqueidentifier] NOT NULL,
	[RecipeVersionId] [uniqueidentifier] NOT NULL,
	[IngredientRef] [uniqueidentifier] NOT NULL,
	[UnitRef] [uniqueidentifier] NOT NULL,
	[Quantity] [decimal](18, 6) NOT NULL,
	[BaseQuantitySnapshot] [decimal](18, 6) NOT NULL,
	[ConversionFactorSnapshot] [decimal](18, 6) NOT NULL,
 CONSTRAINT [PK_RecipeIngredient] PRIMARY KEY CLUSTERED 
(
	[RecipeIngredientId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
ALTER TABLE [dbo].[Item]  WITH CHECK ADD  CONSTRAINT [FK_Item_Category] FOREIGN KEY([CategoryId])
REFERENCES [dbo].[Category] ([CategoryId])
GO
ALTER TABLE [dbo].[Item] CHECK CONSTRAINT [FK_Item_Category]
GO
ALTER TABLE [dbo].[Option]  WITH CHECK ADD  CONSTRAINT [FK_Option_OptionGroup] FOREIGN KEY([OptionGroupId])
REFERENCES [dbo].[OptionGroup] ([OptionGroupId])
GO
ALTER TABLE [dbo].[Option] CHECK CONSTRAINT [FK_Option_OptionGroup]
GO
ALTER TABLE [dbo].[ItemOptionGroup]  WITH CHECK ADD  CONSTRAINT [FK_ItemOptionGroup_Item] FOREIGN KEY([ItemId])
REFERENCES [dbo].[Item] ([ItemId])
GO
ALTER TABLE [dbo].[ItemOptionGroup] CHECK CONSTRAINT [FK_ItemOptionGroup_Item]
GO
ALTER TABLE [dbo].[ItemOptionGroup]  WITH CHECK ADD  CONSTRAINT [FK_ItemOptionGroup_OptionGroup] FOREIGN KEY([OptionGroupId])
REFERENCES [dbo].[OptionGroup] ([OptionGroupId])
GO
ALTER TABLE [dbo].[ItemOptionGroup] CHECK CONSTRAINT [FK_ItemOptionGroup_OptionGroup]
GO
ALTER TABLE [dbo].[Price]  WITH CHECK ADD  CONSTRAINT [FK_Price_Item] FOREIGN KEY([ItemId])
REFERENCES [dbo].[Item] ([ItemId])
GO
ALTER TABLE [dbo].[Price] CHECK CONSTRAINT [FK_Price_Item]
GO
ALTER TABLE [dbo].[RecipeVersion]  WITH CHECK ADD  CONSTRAINT [FK_RecipeVersion_Item] FOREIGN KEY([ItemId])
REFERENCES [dbo].[Item] ([ItemId])
GO
ALTER TABLE [dbo].[RecipeVersion] CHECK CONSTRAINT [FK_RecipeVersion_Item]
GO
ALTER TABLE [dbo].[RecipeVersion]  WITH CHECK ADD  CONSTRAINT [FK_RecipeVersion_Option] FOREIGN KEY([OptionId])
REFERENCES [dbo].[Option] ([OptionId])
GO
ALTER TABLE [dbo].[RecipeVersion] CHECK CONSTRAINT [FK_RecipeVersion_Option]
GO
ALTER TABLE [dbo].[RecipeMedia]  WITH CHECK ADD  CONSTRAINT [FK_RecipeMedia_RecipeVersion] FOREIGN KEY([RecipeVersionId])
REFERENCES [dbo].[RecipeVersion] ([RecipeVersionId])
GO
ALTER TABLE [dbo].[RecipeMedia] CHECK CONSTRAINT [FK_RecipeMedia_RecipeVersion]
GO
ALTER TABLE [dbo].[RecipeIngredient]  WITH CHECK ADD  CONSTRAINT [FK_RecipeIngredient_RecipeVersion] FOREIGN KEY([RecipeVersionId])
REFERENCES [dbo].[RecipeVersion] ([RecipeVersionId])
GO
ALTER TABLE [dbo].[RecipeIngredient] CHECK CONSTRAINT [FK_RecipeIngredient_RecipeVersion]
GO
