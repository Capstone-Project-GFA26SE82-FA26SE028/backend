SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Restaurant](
	[RestaurantId] [uniqueidentifier] NOT NULL,
	[RestaurantName] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[TaxCode] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[Address] [varchar](500) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Hotline] [varchar](20) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[LogoUrl] [varchar](max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[TimeZone] [varchar](100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
 CONSTRAINT [PK_Restaurant] PRIMARY KEY CLUSTERED 
(
	[RestaurantId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Reservation](
	[ReservationId] [uniqueidentifier] NOT NULL,
	[RestaurantId] [uniqueidentifier] NOT NULL,
	[RequestedAreaId] [uniqueidentifier] NOT NULL,
	[ContactName] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Phone] [varchar](20) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[PartySize] [int] NOT NULL,
	[StartAt] [datetime] NOT NULL,
	[EndAt] [datetime] NOT NULL,
	[HoldUntil] [datetime] NULL,
	[Status] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[ApprovedByRef] [uniqueidentifier] NULL,
	[ConfirmedAt] [datetime] NULL,
	[CancelledAt] [datetime] NULL,
	[CancelledByRef] [uniqueidentifier] NULL,
 CONSTRAINT [PK_Reservation] PRIMARY KEY CLUSTERED 
(
	[ReservationId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[TableSession](
	[TableSessionId] [uniqueidentifier] NOT NULL,
	[ReservationId] [uniqueidentifier] NULL,
	[PartySize] [int] NOT NULL,
	[Status] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[OpenedAt] [datetime] NOT NULL,
	[ClosedAt] [datetime] NULL,
	[OpenedByRef] [uniqueidentifier] NULL,
	[JoinCodeHash] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[JoinCodeSetAt] [datetime] NULL,
	[LoyaltyCustomerRef] [uniqueidentifier] NULL,
	[PrebillPrintedAt] [datetime] NULL,
 CONSTRAINT [PK_TableSession] PRIMARY KEY CLUSTERED 
(
	[TableSessionId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Area](
	[AreaId] [uniqueidentifier] NOT NULL,
	[RestaurantId] [uniqueidentifier] NOT NULL,
	[AreaCode] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[AreaName] [varchar](100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[ServiceType] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[SpaceType] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_Area] PRIMARY KEY CLUSTERED 
(
	[AreaId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[OpeningHour](
	[OpeningHourId] [uniqueidentifier] NOT NULL,
	[RestaurantId] [uniqueidentifier] NOT NULL,
	[DayOfWeek] [int] NOT NULL,
	[OpenAt] [time](0) NOT NULL,
	[CloseAt] [time](0) NOT NULL,
 CONSTRAINT [PK_OpeningHour] PRIMARY KEY CLUSTERED 
(
	[OpeningHourId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[GuestAccess](
	[GuestAccessId] [uniqueidentifier] NOT NULL,
	[TableSessionId] [uniqueidentifier] NOT NULL,
	[TokenHash] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[CreatedAt] [datetime] NOT NULL,
	[ExpiredAt] [datetime] NOT NULL,
	[RevokedAt] [datetime] NULL,
 CONSTRAINT [PK_GuestAccess] PRIMARY KEY CLUSTERED 
(
	[GuestAccessId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Table](
	[TableId] [uniqueidentifier] NOT NULL,
	[AreaId] [uniqueidentifier] NOT NULL,
	[TableCode] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Capacity] [int] NOT NULL,
	[PositionX] [decimal](10, 2) NULL,
	[PositionY] [decimal](10, 2) NULL,
	[IsActive] [bit] NOT NULL,
	[CleaningStatus] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Version] [timestamp] NOT NULL,
 CONSTRAINT [PK_Table] PRIMARY KEY CLUSTERED 
(
	[TableId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ServiceRequest](
	[ServiceRequestId] [uniqueidentifier] NOT NULL,
	[TableSessionId] [uniqueidentifier] NOT NULL,
	[Type] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Status] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[RequestedAt] [datetime] NOT NULL,
	[AcknowledgedAt] [datetime] NULL,
	[CompletedAt] [datetime] NULL,
	[HandledByRef] [uniqueidentifier] NULL,
 CONSTRAINT [PK_ServiceRequest] PRIMARY KEY CLUSTERED 
(
	[ServiceRequestId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ReservationTable](
	[ReservationId] [uniqueidentifier] NOT NULL,
	[TableId] [uniqueidentifier] NOT NULL,
 CONSTRAINT [PK_ReservationTable] PRIMARY KEY CLUSTERED 
(
	[ReservationId] ASC,
	[TableId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[SessionTable](
	[SessionTableId] [uniqueidentifier] NOT NULL,
	[TableSessionId] [uniqueidentifier] NOT NULL,
	[TableId] [uniqueidentifier] NOT NULL,
	[AssignedAt] [datetime] NOT NULL,
	[ReleasedAt] [datetime] NULL,
	[ChangedByRef] [uniqueidentifier] NULL,
	[MoveReason] [varchar](500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
 CONSTRAINT [PK_SessionTable] PRIMARY KEY CLUSTERED 
(
	[SessionTableId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Qr](
	[QrId] [uniqueidentifier] NOT NULL,
	[TableId] [uniqueidentifier] NOT NULL,
	[TokenHash] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[CreatedAt] [datetime] NOT NULL,
	[RevokedAt] [datetime] NULL,
 CONSTRAINT [PK_Qr] PRIMARY KEY CLUSTERED 
(
	[QrId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_TableSession_ReservationId] ON [dbo].[TableSession]
(
	[ReservationId] ASC
)
WHERE ([ReservationId] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
ALTER TABLE [dbo].[TableSession]  WITH CHECK ADD  CONSTRAINT [FK_TableSession_Reservation] FOREIGN KEY([ReservationId])
REFERENCES [dbo].[Reservation] ([ReservationId])
GO
ALTER TABLE [dbo].[TableSession] CHECK CONSTRAINT [FK_TableSession_Reservation]
GO
ALTER TABLE [dbo].[Area]  WITH CHECK ADD  CONSTRAINT [FK_Area_Restaurant] FOREIGN KEY([RestaurantId])
REFERENCES [dbo].[Restaurant] ([RestaurantId])
GO
ALTER TABLE [dbo].[Area] CHECK CONSTRAINT [FK_Area_Restaurant]
GO
ALTER TABLE [dbo].[OpeningHour]  WITH CHECK ADD  CONSTRAINT [FK_OpeningHour_Restaurant] FOREIGN KEY([RestaurantId])
REFERENCES [dbo].[Restaurant] ([RestaurantId])
GO
ALTER TABLE [dbo].[OpeningHour] CHECK CONSTRAINT [FK_OpeningHour_Restaurant]
GO
ALTER TABLE [dbo].[GuestAccess]  WITH CHECK ADD  CONSTRAINT [FK_GuestAccess_TableSession] FOREIGN KEY([TableSessionId])
REFERENCES [dbo].[TableSession] ([TableSessionId])
GO
ALTER TABLE [dbo].[GuestAccess] CHECK CONSTRAINT [FK_GuestAccess_TableSession]
GO
ALTER TABLE [dbo].[Table]  WITH CHECK ADD  CONSTRAINT [FK_Table_Area] FOREIGN KEY([AreaId])
REFERENCES [dbo].[Area] ([AreaId])
GO
ALTER TABLE [dbo].[Table] CHECK CONSTRAINT [FK_Table_Area]
GO
ALTER TABLE [dbo].[ServiceRequest]  WITH CHECK ADD  CONSTRAINT [FK_ServiceRequest_TableSession] FOREIGN KEY([TableSessionId])
REFERENCES [dbo].[TableSession] ([TableSessionId])
GO
ALTER TABLE [dbo].[ServiceRequest] CHECK CONSTRAINT [FK_ServiceRequest_TableSession]
GO
ALTER TABLE [dbo].[ReservationTable]  WITH CHECK ADD  CONSTRAINT [FK_ReservationTable_Reservation] FOREIGN KEY([ReservationId])
REFERENCES [dbo].[Reservation] ([ReservationId])
GO
ALTER TABLE [dbo].[ReservationTable] CHECK CONSTRAINT [FK_ReservationTable_Reservation]
GO
ALTER TABLE [dbo].[ReservationTable]  WITH CHECK ADD  CONSTRAINT [FK_ReservationTable_Table] FOREIGN KEY([TableId])
REFERENCES [dbo].[Table] ([TableId])
GO
ALTER TABLE [dbo].[ReservationTable] CHECK CONSTRAINT [FK_ReservationTable_Table]
GO
ALTER TABLE [dbo].[SessionTable]  WITH CHECK ADD  CONSTRAINT [FK_SessionTable_Table] FOREIGN KEY([TableId])
REFERENCES [dbo].[Table] ([TableId])
GO
ALTER TABLE [dbo].[SessionTable] CHECK CONSTRAINT [FK_SessionTable_Table]
GO
ALTER TABLE [dbo].[SessionTable]  WITH CHECK ADD  CONSTRAINT [FK_SessionTable_TableSession] FOREIGN KEY([TableSessionId])
REFERENCES [dbo].[TableSession] ([TableSessionId])
GO
ALTER TABLE [dbo].[SessionTable] CHECK CONSTRAINT [FK_SessionTable_TableSession]
GO
ALTER TABLE [dbo].[Qr]  WITH CHECK ADD  CONSTRAINT [FK_Qr_Table] FOREIGN KEY([TableId])
REFERENCES [dbo].[Table] ([TableId])
GO
ALTER TABLE [dbo].[Qr] CHECK CONSTRAINT [FK_Qr_Table]
GO
CREATE TABLE [dbo].[RestaurantStaff]
(
    [RestaurantId] uniqueidentifier NOT NULL,
    [UserIdRef] uniqueidentifier NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_RestaurantStaff] PRIMARY KEY ([RestaurantId], [UserIdRef]),
    CONSTRAINT [FK_RestaurantStaff_Restaurant] FOREIGN KEY ([RestaurantId])
        REFERENCES [dbo].[Restaurant] ([RestaurantId])
);
GO
ALTER TABLE [dbo].[Reservation] WITH CHECK ADD CONSTRAINT [FK_Reservation_Restaurant]
    FOREIGN KEY ([RestaurantId]) REFERENCES [dbo].[Restaurant] ([RestaurantId]);
GO
ALTER TABLE [dbo].[Reservation] WITH CHECK ADD CONSTRAINT [FK_Reservation_RequestedArea]
    FOREIGN KEY ([RequestedAreaId]) REFERENCES [dbo].[Area] ([AreaId]);
GO
CREATE INDEX [IX_Reservation_RestaurantId_Status_StartAt]
    ON [dbo].[Reservation] ([RestaurantId], [Status], [StartAt]);
GO
CREATE INDEX [IX_Reservation_RequestedAreaId]
    ON [dbo].[Reservation] ([RequestedAreaId]);
GO
