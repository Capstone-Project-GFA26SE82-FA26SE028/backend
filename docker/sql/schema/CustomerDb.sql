SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[LoyaltyTier](
	[LoyaltyTierId] [uniqueidentifier] NOT NULL,
	[LoyaltyTierCode] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[LoyaltyTierName] [varchar](100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[RequiredSpend] [decimal](18, 2) NOT NULL,
	[DiscountPercent] [decimal](5, 2) NOT NULL,
 CONSTRAINT [PK_LoyaltyTier] PRIMARY KEY CLUSTERED 
(
	[LoyaltyTierId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[LoyaltyPolicy](
	[PolicyId] [uniqueidentifier] NOT NULL,
	[Version] [int] NOT NULL,
	[SpendPerPoint] [decimal](18, 2) NOT NULL,
	[MoneyPerRedeemedPoint] [decimal](18, 2) NOT NULL,
	[EffectiveFrom] [datetime] NOT NULL,
 CONSTRAINT [PK_LoyaltyPolicy] PRIMARY KEY CLUSTERED 
(
	[PolicyId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[FeedbackAccess](
	[FeedbackAccessId] [uniqueidentifier] NOT NULL,
	[OrderRef] [uniqueidentifier] NOT NULL,
	[TokenHash] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[ExpiredAt] [datetime] NOT NULL,
	[UsedAt] [datetime] NULL,
 CONSTRAINT [PK_FeedbackAccess] PRIMARY KEY CLUSTERED 
(
	[FeedbackAccessId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Customer](
	[CustomerId] [uniqueidentifier] NOT NULL,
	[NormalizedPhone] [varchar](20) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[CustomerName] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[LoyaltyTierId] [uniqueidentifier] NOT NULL,
	[Version] [timestamp] NOT NULL,
 CONSTRAINT [PK_Customer] PRIMARY KEY CLUSTERED 
(
	[CustomerId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Feedback](
	[FeedbackId] [uniqueidentifier] NOT NULL,
	[AccessId] [uniqueidentifier] NOT NULL,
	[OrderRef] [uniqueidentifier] NOT NULL,
	[CustomerId] [uniqueidentifier] NULL,
	[Star] [int] NOT NULL,
	[Comment] [varchar](max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[SubmittedAt] [datetime] NOT NULL,
 CONSTRAINT [PK_Feedback] PRIMARY KEY CLUSTERED 
(
	[FeedbackId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_Feedback_AccessId] UNIQUE NONCLUSTERED 
(
	[AccessId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PointReservation](
	[PointReservationId] [uniqueidentifier] NOT NULL,
	[CustomerId] [uniqueidentifier] NOT NULL,
	[BillRef] [uniqueidentifier] NOT NULL,
	[Point] [int] NOT NULL,
	[Status] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[ExpiredAt] [datetime] NOT NULL,
 CONSTRAINT [PK_PointReservation] PRIMARY KEY CLUSTERED 
(
	[PointReservationId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[LoyaltyEntry](
	[LoyaltyEntryId] [uniqueidentifier] NOT NULL,
	[CustomerId] [uniqueidentifier] NOT NULL,
	[BillRef] [uniqueidentifier] NULL,
	[RefundRef] [uniqueidentifier] NULL,
	[PolicyId] [uniqueidentifier] NOT NULL,
	[Type] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[SignedPoint] [int] NOT NULL,
	[EligibleSpend] [decimal](18, 2) NOT NULL,
	[SourceEventId] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[PostedAt] [datetime] NOT NULL,
 CONSTRAINT [PK_LoyaltyEntry] PRIMARY KEY CLUSTERED 
(
	[LoyaltyEntryId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[FeedbackItem](
	[FeedbackItemId] [uniqueidentifier] NOT NULL,
	[FeedbackId] [uniqueidentifier] NOT NULL,
	[OrderItemRef] [uniqueidentifier] NOT NULL,
	[Star] [int] NOT NULL,
	[Comment] [varchar](max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
 CONSTRAINT [PK_FeedbackItem] PRIMARY KEY CLUSTERED 
(
	[FeedbackItemId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
ALTER TABLE [dbo].[Customer]  WITH CHECK ADD  CONSTRAINT [FK_Customer_LoyaltyTier] FOREIGN KEY([LoyaltyTierId])
REFERENCES [dbo].[LoyaltyTier] ([LoyaltyTierId])
GO
ALTER TABLE [dbo].[Customer] CHECK CONSTRAINT [FK_Customer_LoyaltyTier]
GO
ALTER TABLE [dbo].[Feedback]  WITH CHECK ADD  CONSTRAINT [FK_Feedback_Customer] FOREIGN KEY([CustomerId])
REFERENCES [dbo].[Customer] ([CustomerId])
GO
ALTER TABLE [dbo].[Feedback] CHECK CONSTRAINT [FK_Feedback_Customer]
GO
ALTER TABLE [dbo].[Feedback]  WITH CHECK ADD  CONSTRAINT [FK_Feedback_FeedbackAccess] FOREIGN KEY([AccessId])
REFERENCES [dbo].[FeedbackAccess] ([FeedbackAccessId])
GO
ALTER TABLE [dbo].[Feedback] CHECK CONSTRAINT [FK_Feedback_FeedbackAccess]
GO
ALTER TABLE [dbo].[PointReservation]  WITH CHECK ADD  CONSTRAINT [FK_PointReservation_Customer] FOREIGN KEY([CustomerId])
REFERENCES [dbo].[Customer] ([CustomerId])
GO
ALTER TABLE [dbo].[PointReservation] CHECK CONSTRAINT [FK_PointReservation_Customer]
GO
ALTER TABLE [dbo].[LoyaltyEntry]  WITH CHECK ADD  CONSTRAINT [FK_LoyaltyEntry_Customer] FOREIGN KEY([CustomerId])
REFERENCES [dbo].[Customer] ([CustomerId])
GO
ALTER TABLE [dbo].[LoyaltyEntry] CHECK CONSTRAINT [FK_LoyaltyEntry_Customer]
GO
ALTER TABLE [dbo].[LoyaltyEntry]  WITH CHECK ADD  CONSTRAINT [FK_LoyaltyEntry_LoyaltyPolicy] FOREIGN KEY([PolicyId])
REFERENCES [dbo].[LoyaltyPolicy] ([PolicyId])
GO
ALTER TABLE [dbo].[LoyaltyEntry] CHECK CONSTRAINT [FK_LoyaltyEntry_LoyaltyPolicy]
GO
ALTER TABLE [dbo].[FeedbackItem]  WITH CHECK ADD  CONSTRAINT [FK_FeedbackItem_Feedback] FOREIGN KEY([FeedbackId])
REFERENCES [dbo].[Feedback] ([FeedbackId])
GO
ALTER TABLE [dbo].[FeedbackItem] CHECK CONSTRAINT [FK_FeedbackItem_Feedback]
GO
