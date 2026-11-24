SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE TABLE dbo.[CancellationRequest]
(
    [CancellationRequestId] uniqueidentifier NOT NULL,
    [OrderItemId] uniqueidentifier NOT NULL,
    [Quantity] int NOT NULL,
    [Reason] nvarchar(1000) NOT NULL,
    [Source] varchar(20) NOT NULL CONSTRAINT [DF_CancellationRequest_Source] DEFAULT ('Customer'),
    [RequestedByRef] uniqueidentifier NOT NULL,
    [DecidedByRef] uniqueidentifier NULL,
    [DecidedAt] datetime NULL,
    [DecisionReason] nvarchar(1000) NULL,
    [Status] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [RequestedAt] datetime NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[Cart]
(
    [CartId] uniqueidentifier NOT NULL,
    [TableSessionRef] uniqueidentifier NULL,
    [GuestAccessRef] uniqueidentifier NULL,
    [ExpiredAt] datetime NOT NULL,
    [CustomerUserRef] uniqueidentifier NULL,
    [RestaurantRef] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[CartItem]
(
    [CartItemId] uniqueidentifier NOT NULL,
    [CartId] uniqueidentifier NOT NULL,
    [ItemRef] uniqueidentifier NOT NULL,
    [Quantity] int NOT NULL,
    [Note] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[CartItemOption]
(
    [CartItemOptionId] uniqueidentifier NOT NULL,
    [CartItemId] uniqueidentifier NOT NULL,
    [OptionRef] uniqueidentifier NOT NULL,
    [Quantity] int NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[CheckoutLock]
(
    [CheckoutId] uniqueidentifier NOT NULL,
    [BillIdRef] uniqueidentifier NULL,
    [TableSessionRef] uniqueidentifier NOT NULL,
    [SourceHash] char(64) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [Status] varchar(20) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [CreatedAt] datetime2(7) NOT NULL,
    [ReleasedAt] datetime2(7) NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[Event]
(
    [EventId] uniqueidentifier NOT NULL,
    [KitchenTaskId] uniqueidentifier NOT NULL,
    [FromStatus] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [ToStatus] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [ActorUserRef] uniqueidentifier NULL,
    [OccurredAt] datetime NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[KitchenAssignment]
(
    [KitchenStationId] uniqueidentifier NOT NULL,
    [UserRef] uniqueidentifier NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[KitchenStation]
(
    [KitchenStationId] uniqueidentifier NOT NULL,
    [KitchenStationCode] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [KitchenStationName] nvarchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [IsActive] bit NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[KitchenTask]
(
    [KitchenTaskId] uniqueidentifier NOT NULL,
    [KitchenStationId] uniqueidentifier NOT NULL,
    [OrderItemId] uniqueidentifier NOT NULL,
    [BatchNumber] int NOT NULL,
    [Quantity] int NOT NULL,
    [Status] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [Priority] int NOT NULL,
    [EstimatedMinutes] int NULL,
    [QueuedAt] datetime NOT NULL,
    [CookingAt] datetime NULL,
    [ReadyAt] datetime NULL,
    [Version] timestamp NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[Order]
(
    [OrderId] uniqueidentifier NOT NULL,
    [OrderNumber] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [TableSessionRef] uniqueidentifier NULL,
    [CustomerRef] uniqueidentifier NULL,
    [Channel] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [CreatedByRef] uniqueidentifier NULL,
    [IdempotencyKey] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [RequestHash] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [CreatedAt] datetime NOT NULL,
    [Status] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [Version] timestamp NOT NULL,
    [ConfirmedAt] datetime NULL,
    [ConfirmedByRef] uniqueidentifier NULL,
    [RestaurantRef] uniqueidentifier NULL,
    [CustomerUserRef] uniqueidentifier NULL,
    [PickupName] nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [PickupPhone] nvarchar(20) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [PickupAt] datetime2(7) NULL,
    [PickupCode] nvarchar(64) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [PaymentStatus] nvarchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [CheckoutUrl] nvarchar(2048) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [CancellationRequested] bit NOT NULL CONSTRAINT [DF_Order_CancellationRequested] DEFAULT ((0)),
    [RejectionRequested] bit NOT NULL CONSTRAINT [DF_Order_RejectionRequested] DEFAULT ((0)),
    [CancellationReason] nvarchar(1000) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [ReadyForPickupAt] datetime2(7) NULL,
    [CompletedAt] datetime2(7) NULL,
    [RefundedAt] datetime2(7) NULL,
    [RefundBankName] nvarchar(100) NULL,
    [RefundAccountNumber] nvarchar(34) NULL,
    [RefundAccountHolder] nvarchar(150) NULL,
    [RefundReference] nvarchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NULL
);
GO

CREATE TABLE dbo.[OrderItem]
(
    [OrderItemId] uniqueidentifier NOT NULL,
    [OrderId] uniqueidentifier NOT NULL,
    [ItemRef] uniqueidentifier NOT NULL,
    [RecipeVersionRef] uniqueidentifier NULL,
    [OriginalTableRef] uniqueidentifier NULL,
    [ComboGroupId] uniqueidentifier NULL,
    [NameSnapshot] nvarchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [UnitPriceSnapshot] decimal(18,2) NOT NULL,
    [Quantity] int NOT NULL,
    [CancelledQuantity] int NOT NULL,
    [Note] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [Status] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [Version] timestamp NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[OrderItemOption]
(
    [Id] uniqueidentifier NOT NULL,
    [OrderItemId] uniqueidentifier NOT NULL,
    [OptionRef] uniqueidentifier NOT NULL,
    [NameSnapshot] nvarchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [ExtraPriceSnapshot] decimal(18,2) NOT NULL,
    [Quantity] int NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[ServiceRecord]
(
    [ServiceRecordId] uniqueidentifier NOT NULL,
    [OrderItemId] uniqueidentifier NOT NULL,
    [QuantityServed] int NOT NULL,
    [WaiterUserRef] uniqueidentifier NOT NULL,
    [ServedAt] datetime NOT NULL,
    [IdempotencyKey] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE NONCLUSTERED INDEX [IX_CancellationRequest_RestaurantIsolation] ON dbo.[CancellationRequest] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[CancellationRequest] ADD CONSTRAINT [PK_CancellationRequest] PRIMARY KEY CLUSTERED ([CancellationRequestId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_Cart_OnlineOwner] ON dbo.[Cart] ([RestaurantRef] ASC, [CustomerUserRef] ASC, [ExpiredAt] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_Cart_RestaurantIsolation] ON dbo.[Cart] ([RestaurantRef] ASC);
GO
ALTER TABLE dbo.[Cart] ADD CONSTRAINT [PK_Cart] PRIMARY KEY CLUSTERED ([CartId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_CartItem_RestaurantIsolation] ON dbo.[CartItem] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[CartItem] ADD CONSTRAINT [PK_CartItem] PRIMARY KEY CLUSTERED ([CartItemId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_CartItemOption_RestaurantIsolation] ON dbo.[CartItemOption] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[CartItemOption] ADD CONSTRAINT [PK_CartItemOption] PRIMARY KEY CLUSTERED ([CartItemOptionId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_CheckoutLock_RestaurantIsolation] ON dbo.[CheckoutLock] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[CheckoutLock] ADD CONSTRAINT [PK_CheckoutLock] PRIMARY KEY CLUSTERED ([CheckoutId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_CheckoutLock_ActiveSession] ON dbo.[CheckoutLock] ([TableSessionRef] ASC) WHERE ([ReleasedAt] IS NULL);
GO

CREATE NONCLUSTERED INDEX [IX_Event_RestaurantIsolation] ON dbo.[Event] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[Event] ADD CONSTRAINT [PK_Event] PRIMARY KEY CLUSTERED ([EventId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_KitchenAssignment_RestaurantIsolation] ON dbo.[KitchenAssignment] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[KitchenAssignment] ADD CONSTRAINT [PK_KitchenAssignment] PRIMARY KEY CLUSTERED ([KitchenStationId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_KitchenStation_RestaurantIsolation] ON dbo.[KitchenStation] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[KitchenStation] ADD CONSTRAINT [PK_KitchenStation] PRIMARY KEY CLUSTERED ([KitchenStationId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_KitchenTask_RestaurantIsolation] ON dbo.[KitchenTask] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[KitchenTask] ADD CONSTRAINT [PK_KitchenTask] PRIMARY KEY CLUSTERED ([KitchenTaskId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_Order_RestaurantIsolation] ON dbo.[Order] ([RestaurantRef] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_Order_RestaurantRef_Channel_CreatedAt] ON dbo.[Order] ([RestaurantRef] ASC, [Channel] ASC, [CreatedAt] ASC);
GO
ALTER TABLE dbo.[Order] ADD CONSTRAINT [PK_Order] PRIMARY KEY CLUSTERED ([OrderId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_OrderItem_RestaurantIsolation] ON dbo.[OrderItem] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[OrderItem] ADD CONSTRAINT [PK_OrderItem] PRIMARY KEY CLUSTERED ([OrderItemId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_OrderItemOption_RestaurantIsolation] ON dbo.[OrderItemOption] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[OrderItemOption] ADD CONSTRAINT [PK_OrderItemOption] PRIMARY KEY CLUSTERED ([Id] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_ServiceRecord_RestaurantIsolation] ON dbo.[ServiceRecord] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[ServiceRecord] ADD CONSTRAINT [PK_ServiceRecord] PRIMARY KEY CLUSTERED ([ServiceRecordId] ASC);
GO

ALTER TABLE dbo.[CancellationRequest] WITH CHECK ADD CONSTRAINT [FK_CancellationRequest_OrderItem] FOREIGN KEY ([OrderItemId]) REFERENCES dbo.[OrderItem] ([OrderItemId]);
GO
ALTER TABLE dbo.[CartItem] WITH CHECK ADD CONSTRAINT [FK_CartItem_Cart] FOREIGN KEY ([CartId]) REFERENCES dbo.[Cart] ([CartId]);
GO
ALTER TABLE dbo.[CartItemOption] WITH CHECK ADD CONSTRAINT [FK_CartItemOption_CartItem] FOREIGN KEY ([CartItemId]) REFERENCES dbo.[CartItem] ([CartItemId]);
GO
ALTER TABLE dbo.[Event] WITH CHECK ADD CONSTRAINT [FK_Event_KitchenTask] FOREIGN KEY ([KitchenTaskId]) REFERENCES dbo.[KitchenTask] ([KitchenTaskId]);
GO
ALTER TABLE dbo.[KitchenAssignment] WITH CHECK ADD CONSTRAINT [FK_KitchenAssignment_KitchenStation] FOREIGN KEY ([KitchenStationId]) REFERENCES dbo.[KitchenStation] ([KitchenStationId]);
GO
ALTER TABLE dbo.[KitchenTask] WITH CHECK ADD CONSTRAINT [FK_KitchenTask_KitchenStation] FOREIGN KEY ([KitchenStationId]) REFERENCES dbo.[KitchenStation] ([KitchenStationId]);
GO
ALTER TABLE dbo.[KitchenTask] WITH CHECK ADD CONSTRAINT [FK_KitchenTask_OrderItem] FOREIGN KEY ([OrderItemId]) REFERENCES dbo.[OrderItem] ([OrderItemId]);
GO
ALTER TABLE dbo.[OrderItem] WITH CHECK ADD CONSTRAINT [FK_OrderItem_Order] FOREIGN KEY ([OrderId]) REFERENCES dbo.[Order] ([OrderId]);
GO
ALTER TABLE dbo.[OrderItemOption] WITH CHECK ADD CONSTRAINT [FK_OrderItemOption_OrderItem] FOREIGN KEY ([OrderItemId]) REFERENCES dbo.[OrderItem] ([OrderItemId]);
GO
ALTER TABLE dbo.[ServiceRecord] WITH CHECK ADD CONSTRAINT [FK_ServiceRecord_OrderItem] FOREIGN KEY ([OrderItemId]) REFERENCES dbo.[OrderItem] ([OrderItemId]);
GO

CREATE TABLE dbo.NotificationOutbox (
    NotificationOutboxId uniqueidentifier NOT NULL CONSTRAINT PK_NotificationOutbox PRIMARY KEY,
    RestaurantId uniqueidentifier NOT NULL,
    EventKey nvarchar(200) NOT NULL,
    Payload nvarchar(max) NOT NULL,
    CreatedAt datetime2 NOT NULL,
    NextAttemptAt datetime2 NOT NULL,
    ProcessedAt datetime2 NULL,
    Attempts int NOT NULL,
    LastError nvarchar(100) NULL
);
CREATE UNIQUE INDEX IX_NotificationOutbox_EventKey ON dbo.NotificationOutbox(EventKey);
CREATE INDEX IX_NotificationOutbox_ProcessedAt_NextAttemptAt ON dbo.NotificationOutbox(ProcessedAt, NextAttemptAt);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UX_CancellationRequest_PendingItem] ON dbo.[CancellationRequest] ([OrderItemId]) WHERE [Status] = 'Pending';
GO
