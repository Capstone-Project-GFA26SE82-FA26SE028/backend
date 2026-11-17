SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE TABLE dbo.[Customer]
(
    [CustomerId] uniqueidentifier NOT NULL,
    [NormalizedPhone] varchar(20) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [CustomerName] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [LoyaltyTierId] uniqueidentifier NOT NULL,
    [Version] timestamp NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[Feedback]
(
    [FeedbackId] uniqueidentifier NOT NULL,
    [AccessId] uniqueidentifier NOT NULL,
    [OrderRef] uniqueidentifier NOT NULL,
    [CustomerId] uniqueidentifier NULL,
    [Star] int NOT NULL,
    [Comment] varchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [SubmittedAt] datetime NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[FeedbackAccess]
(
    [FeedbackAccessId] uniqueidentifier NOT NULL,
    [OrderRef] uniqueidentifier NOT NULL,
    [TokenHash] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [ExpiredAt] datetime NOT NULL,
    [UsedAt] datetime NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[FeedbackItem]
(
    [FeedbackItemId] uniqueidentifier NOT NULL,
    [FeedbackId] uniqueidentifier NOT NULL,
    [OrderItemRef] uniqueidentifier NOT NULL,
    [Star] int NOT NULL,
    [Comment] varchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[LoyaltyEntry]
(
    [LoyaltyEntryId] uniqueidentifier NOT NULL,
    [CustomerId] uniqueidentifier NOT NULL,
    [BillRef] uniqueidentifier NULL,
    [RefundRef] uniqueidentifier NULL,
    [PolicyId] uniqueidentifier NOT NULL,
    [Type] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [SignedPoint] int NOT NULL,
    [EligibleSpend] decimal(18,2) NOT NULL,
    [SourceEventId] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [PostedAt] datetime NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[LoyaltyPolicy]
(
    [PolicyId] uniqueidentifier NOT NULL,
    [Version] int NOT NULL,
    [SpendPerPoint] decimal(18,2) NOT NULL,
    [MoneyPerRedeemedPoint] decimal(18,2) NOT NULL,
    [EffectiveFrom] datetime NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[LoyaltyTier]
(
    [LoyaltyTierId] uniqueidentifier NOT NULL,
    [LoyaltyTierCode] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [LoyaltyTierName] varchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [RequiredSpend] decimal(18,2) NOT NULL,
    [DiscountPercent] decimal(5,2) NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[PointReservation]
(
    [PointReservationId] uniqueidentifier NOT NULL,
    [CustomerId] uniqueidentifier NOT NULL,
    [BillRef] uniqueidentifier NOT NULL,
    [Point] int NOT NULL,
    [Status] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [ExpiredAt] datetime NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE NONCLUSTERED INDEX [IX_Customer_RestaurantIsolation] ON dbo.[Customer] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[Customer] ADD CONSTRAINT [PK_Customer] PRIMARY KEY CLUSTERED ([CustomerId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_Feedback_RestaurantIsolation] ON dbo.[Feedback] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[Feedback] ADD CONSTRAINT [PK_Feedback] PRIMARY KEY CLUSTERED ([FeedbackId] ASC);
GO
ALTER TABLE dbo.[Feedback] ADD CONSTRAINT [UQ_Feedback_AccessId] UNIQUE NONCLUSTERED ([AccessId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_FeedbackAccess_RestaurantIsolation] ON dbo.[FeedbackAccess] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[FeedbackAccess] ADD CONSTRAINT [PK_FeedbackAccess] PRIMARY KEY CLUSTERED ([FeedbackAccessId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_FeedbackItem_RestaurantIsolation] ON dbo.[FeedbackItem] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[FeedbackItem] ADD CONSTRAINT [PK_FeedbackItem] PRIMARY KEY CLUSTERED ([FeedbackItemId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_LoyaltyEntry_RestaurantIsolation] ON dbo.[LoyaltyEntry] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[LoyaltyEntry] ADD CONSTRAINT [PK_LoyaltyEntry] PRIMARY KEY CLUSTERED ([LoyaltyEntryId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_LoyaltyPolicy_RestaurantIsolation] ON dbo.[LoyaltyPolicy] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[LoyaltyPolicy] ADD CONSTRAINT [PK_LoyaltyPolicy] PRIMARY KEY CLUSTERED ([PolicyId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_LoyaltyTier_RestaurantIsolation] ON dbo.[LoyaltyTier] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[LoyaltyTier] ADD CONSTRAINT [PK_LoyaltyTier] PRIMARY KEY CLUSTERED ([LoyaltyTierId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_PointReservation_RestaurantIsolation] ON dbo.[PointReservation] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[PointReservation] ADD CONSTRAINT [PK_PointReservation] PRIMARY KEY CLUSTERED ([PointReservationId] ASC);
GO

ALTER TABLE dbo.[Customer] WITH CHECK ADD CONSTRAINT [FK_Customer_LoyaltyTier] FOREIGN KEY ([LoyaltyTierId]) REFERENCES dbo.[LoyaltyTier] ([LoyaltyTierId]);
GO
ALTER TABLE dbo.[Feedback] WITH CHECK ADD CONSTRAINT [FK_Feedback_Customer] FOREIGN KEY ([CustomerId]) REFERENCES dbo.[Customer] ([CustomerId]);
GO
ALTER TABLE dbo.[Feedback] WITH CHECK ADD CONSTRAINT [FK_Feedback_FeedbackAccess] FOREIGN KEY ([AccessId]) REFERENCES dbo.[FeedbackAccess] ([FeedbackAccessId]);
GO
ALTER TABLE dbo.[FeedbackItem] WITH CHECK ADD CONSTRAINT [FK_FeedbackItem_Feedback] FOREIGN KEY ([FeedbackId]) REFERENCES dbo.[Feedback] ([FeedbackId]);
GO
ALTER TABLE dbo.[LoyaltyEntry] WITH CHECK ADD CONSTRAINT [FK_LoyaltyEntry_Customer] FOREIGN KEY ([CustomerId]) REFERENCES dbo.[Customer] ([CustomerId]);
GO
ALTER TABLE dbo.[LoyaltyEntry] WITH CHECK ADD CONSTRAINT [FK_LoyaltyEntry_LoyaltyPolicy] FOREIGN KEY ([PolicyId]) REFERENCES dbo.[LoyaltyPolicy] ([PolicyId]);
GO
ALTER TABLE dbo.[PointReservation] WITH CHECK ADD CONSTRAINT [FK_PointReservation_Customer] FOREIGN KEY ([CustomerId]) REFERENCES dbo.[Customer] ([CustomerId]);
GO
