SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Order](
	[OrderId] [uniqueidentifier] NOT NULL,
	[OrderNumber] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[TableSessionRef] [uniqueidentifier] NULL,
	[CustomerRef] [uniqueidentifier] NULL,
	[Channel] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[CreatedByRef] [uniqueidentifier] NULL,
	[IdempotencyKey] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[RequestHash] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[CreatedAt] [datetime] NOT NULL,
	[Status] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Version] [timestamp] NOT NULL,
	[ConfirmedAt] [datetime] NULL,
	[ConfirmedByRef] [uniqueidentifier] NULL,
 CONSTRAINT [PK_Order] PRIMARY KEY CLUSTERED 
(
	[OrderId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Cart](
	[CartId] [uniqueidentifier] NOT NULL,
	[TableSessionRef] [uniqueidentifier] NOT NULL,
	[GuestAccessRef] [uniqueidentifier] NULL,
	[ExpiredAt] [datetime] NOT NULL,
 CONSTRAINT [PK_Cart] PRIMARY KEY CLUSTERED 
(
	[CartId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[KitchenStation](
	[KitchenStationId] [uniqueidentifier] NOT NULL,
	[KitchenStationCode] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[KitchenStationName] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_KitchenStation] PRIMARY KEY CLUSTERED 
(
	[KitchenStationId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[KitchenAssignment](
	[KitchenStationId] [uniqueidentifier] NOT NULL,
	[UserRef] [uniqueidentifier] NOT NULL,
 CONSTRAINT [PK_KitchenAssignment] PRIMARY KEY CLUSTERED 
(
	[KitchenStationId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[CartItem](
	[CartItemId] [uniqueidentifier] NOT NULL,
	[CartId] [uniqueidentifier] NOT NULL,
	[ItemRef] [uniqueidentifier] NOT NULL,
	[Quantity] [int] NOT NULL,
	[Note] [varchar](max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
 CONSTRAINT [PK_CartItem] PRIMARY KEY CLUSTERED 
(
	[CartItemId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[OrderItem](
	[OrderItemId] [uniqueidentifier] NOT NULL,
	[OrderId] [uniqueidentifier] NOT NULL,
	[ItemRef] [uniqueidentifier] NOT NULL,
	[RecipeVersionRef] [uniqueidentifier] NULL,
	[OriginalTableRef] [uniqueidentifier] NULL,
	[ComboGroupId] [uniqueidentifier] NULL,
	[NameSnapshot] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[UnitPriceSnapshot] [decimal](18, 2) NOT NULL,
	[Quantity] [int] NOT NULL,
	[CancelledQuantity] [int] NOT NULL,
	[Note] [varchar](max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[Status] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Version] [timestamp] NOT NULL,
 CONSTRAINT [PK_OrderItem] PRIMARY KEY CLUSTERED 
(
	[OrderItemId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[CartItemOption](
	[CartItemOptionId] [uniqueidentifier] NOT NULL,
	[CartItemId] [uniqueidentifier] NOT NULL,
	[OptionRef] [uniqueidentifier] NOT NULL,
	[Quantity] [int] NOT NULL,
 CONSTRAINT [PK_CartItemOption] PRIMARY KEY CLUSTERED 
(
	[CartItemOptionId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ServiceRecord](
	[ServiceRecordId] [uniqueidentifier] NOT NULL,
	[OrderItemId] [uniqueidentifier] NOT NULL,
	[QuantityServed] [int] NOT NULL,
	[WaiterUserRef] [uniqueidentifier] NOT NULL,
	[ServedAt] [datetime] NOT NULL,
	[IdempotencyKey] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
 CONSTRAINT [PK_ServiceRecord] PRIMARY KEY CLUSTERED 
(
	[ServiceRecordId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[OrderItemOption](
	[Id] [uniqueidentifier] NOT NULL,
	[OrderItemId] [uniqueidentifier] NOT NULL,
	[OptionRef] [uniqueidentifier] NOT NULL,
	[NameSnapshot] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[ExtraPriceSnapshot] [decimal](18, 2) NOT NULL,
	[Quantity] [int] NOT NULL,
 CONSTRAINT [PK_OrderItemOption] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[KitchenTask](
	[KitchenTaskId] [uniqueidentifier] NOT NULL,
	[KitchenStationId] [uniqueidentifier] NOT NULL,
	[OrderItemId] [uniqueidentifier] NOT NULL,
	[BatchNumber] [int] NOT NULL,
	[Quantity] [int] NOT NULL,
	[Status] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Priority] [int] NOT NULL,
	[EstimatedMinutes] [int] NULL,
	[QueuedAt] [datetime] NOT NULL,
	[CookingAt] [datetime] NULL,
	[ReadyAt] [datetime] NULL,
	[Version] [timestamp] NOT NULL,
 CONSTRAINT [PK_KitchenTask] PRIMARY KEY CLUSTERED 
(
	[KitchenTaskId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[CancellationRequest](
	[CancellationRequestId] [uniqueidentifier] NOT NULL,
	[OrderItemId] [uniqueidentifier] NOT NULL,
	[Quantity] [int] NOT NULL,
	[Reason] [varchar](max) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[RequestedByRef] [uniqueidentifier] NOT NULL,
	[DecidedByRef] [uniqueidentifier] NULL,
	[Status] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[RequestedAt] [datetime] NOT NULL,
 CONSTRAINT [PK_CancellationRequest] PRIMARY KEY CLUSTERED 
(
	[CancellationRequestId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Event](
	[EventId] [uniqueidentifier] NOT NULL,
	[KitchenTaskId] [uniqueidentifier] NOT NULL,
	[FromStatus] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[ToStatus] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[ActorUserRef] [uniqueidentifier] NULL,
	[OccurredAt] [datetime] NOT NULL,
 CONSTRAINT [PK_Event] PRIMARY KEY CLUSTERED 
(
	[EventId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
ALTER TABLE [dbo].[KitchenAssignment]  WITH CHECK ADD  CONSTRAINT [FK_KitchenAssignment_KitchenStation] FOREIGN KEY([KitchenStationId])
REFERENCES [dbo].[KitchenStation] ([KitchenStationId])
GO
ALTER TABLE [dbo].[KitchenAssignment] CHECK CONSTRAINT [FK_KitchenAssignment_KitchenStation]
GO
ALTER TABLE [dbo].[CartItem]  WITH CHECK ADD  CONSTRAINT [FK_CartItem_Cart] FOREIGN KEY([CartId])
REFERENCES [dbo].[Cart] ([CartId])
GO
ALTER TABLE [dbo].[CartItem] CHECK CONSTRAINT [FK_CartItem_Cart]
GO
ALTER TABLE [dbo].[OrderItem]  WITH CHECK ADD  CONSTRAINT [FK_OrderItem_Order] FOREIGN KEY([OrderId])
REFERENCES [dbo].[Order] ([OrderId])
GO
ALTER TABLE [dbo].[OrderItem] CHECK CONSTRAINT [FK_OrderItem_Order]
GO
ALTER TABLE [dbo].[CartItemOption]  WITH CHECK ADD  CONSTRAINT [FK_CartItemOption_CartItem] FOREIGN KEY([CartItemId])
REFERENCES [dbo].[CartItem] ([CartItemId])
GO
ALTER TABLE [dbo].[CartItemOption] CHECK CONSTRAINT [FK_CartItemOption_CartItem]
GO
ALTER TABLE [dbo].[ServiceRecord]  WITH CHECK ADD  CONSTRAINT [FK_ServiceRecord_OrderItem] FOREIGN KEY([OrderItemId])
REFERENCES [dbo].[OrderItem] ([OrderItemId])
GO
ALTER TABLE [dbo].[ServiceRecord] CHECK CONSTRAINT [FK_ServiceRecord_OrderItem]
GO
ALTER TABLE [dbo].[OrderItemOption]  WITH CHECK ADD  CONSTRAINT [FK_OrderItemOption_OrderItem] FOREIGN KEY([OrderItemId])
REFERENCES [dbo].[OrderItem] ([OrderItemId])
GO
ALTER TABLE [dbo].[OrderItemOption] CHECK CONSTRAINT [FK_OrderItemOption_OrderItem]
GO
ALTER TABLE [dbo].[KitchenTask]  WITH CHECK ADD  CONSTRAINT [FK_KitchenTask_KitchenStation] FOREIGN KEY([KitchenStationId])
REFERENCES [dbo].[KitchenStation] ([KitchenStationId])
GO
ALTER TABLE [dbo].[KitchenTask] CHECK CONSTRAINT [FK_KitchenTask_KitchenStation]
GO
ALTER TABLE [dbo].[KitchenTask]  WITH CHECK ADD  CONSTRAINT [FK_KitchenTask_OrderItem] FOREIGN KEY([OrderItemId])
REFERENCES [dbo].[OrderItem] ([OrderItemId])
GO
ALTER TABLE [dbo].[KitchenTask] CHECK CONSTRAINT [FK_KitchenTask_OrderItem]
GO
ALTER TABLE [dbo].[CancellationRequest]  WITH CHECK ADD  CONSTRAINT [FK_CancellationRequest_OrderItem] FOREIGN KEY([OrderItemId])
REFERENCES [dbo].[OrderItem] ([OrderItemId])
GO
ALTER TABLE [dbo].[CancellationRequest] CHECK CONSTRAINT [FK_CancellationRequest_OrderItem]
GO
ALTER TABLE [dbo].[Event]  WITH CHECK ADD  CONSTRAINT [FK_Event_KitchenTask] FOREIGN KEY([KitchenTaskId])
REFERENCES [dbo].[KitchenTask] ([KitchenTaskId])
GO
ALTER TABLE [dbo].[Event] CHECK CONSTRAINT [FK_Event_KitchenTask]
GO
