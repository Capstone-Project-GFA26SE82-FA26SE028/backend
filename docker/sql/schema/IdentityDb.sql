SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Role](
	[RoleId] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[RoleName] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Description] [varchar](max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
 CONSTRAINT [PK_Role] PRIMARY KEY CLUSTERED 
(
	[RoleId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[User](
    [RestaurantId] [uniqueidentifier] NULL,
    [ProvisioningPending] [bit] NOT NULL CONSTRAINT [DF_User_ProvisioningPending] DEFAULT (0),
	[UserId] [uniqueidentifier] NOT NULL,
	[RoleId] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[UserName] [varchar](100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Email] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[PhoneNumber] [varchar](20) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[FullName] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[AvatarUrl] [varchar](max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[PasswordHash] [varchar](max) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[IsActive] [bit] NOT NULL,
	[CreatedAt] [datetime] NOT NULL,
	[UpdatedAt] [datetime] NULL,
 CONSTRAINT [PK_User] PRIMARY KEY CLUSTERED 
(
	[UserId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[UserSession](
	[UserSessionId] [uniqueidentifier] NOT NULL,
	[UserId] [uniqueidentifier] NOT NULL,
	[IpAddress] [varchar](45) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[UserAgent] [varchar](max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[DeviceType] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[RefreshTokenHash] [varchar](64) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[LoggedInAt] [datetime] NOT NULL,
	[ExpiresAt] [datetime] NOT NULL,
	[RevokedAt] [datetime] NULL,
 CONSTRAINT [PK_UserSession] PRIMARY KEY CLUSTERED 
(
	[UserSessionId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_UserSession_RefreshTokenHash] UNIQUE NONCLUSTERED 
(
	[RefreshTokenHash] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[AuditLog](
	[AuditLogId] [uniqueidentifier] NOT NULL,
	[UserId] [uniqueidentifier] NULL,
	[Action] [varchar](100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[EntityName] [varchar](100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[EntityId] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[OldValue] [varchar](max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[NewValue] [varchar](max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[IpAddress] [varchar](45) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[CreatedAt] [datetime] NOT NULL,
 CONSTRAINT [PK_AuditLog] PRIMARY KEY CLUSTERED 
(
	[AuditLogId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
ALTER TABLE [dbo].[User]  WITH CHECK ADD  CONSTRAINT [FK_User_Role] FOREIGN KEY([RoleId])
REFERENCES [dbo].[Role] ([RoleId])
GO
ALTER TABLE [dbo].[User] CHECK CONSTRAINT [FK_User_Role]
GO
ALTER TABLE [dbo].[UserSession]  WITH CHECK ADD  CONSTRAINT [FK_UserSession_User] FOREIGN KEY([UserId])
REFERENCES [dbo].[User] ([UserId])
GO
ALTER TABLE [dbo].[UserSession] CHECK CONSTRAINT [FK_UserSession_User]
GO
ALTER TABLE [dbo].[AuditLog]  WITH CHECK ADD  CONSTRAINT [FK_AuditLog_User] FOREIGN KEY([UserId])
REFERENCES [dbo].[User] ([UserId])
GO
ALTER TABLE [dbo].[AuditLog] CHECK CONSTRAINT [FK_AuditLog_User]
GO

CREATE INDEX IX_User_RestaurantId ON dbo.[User](RestaurantId);
GO

CREATE TABLE dbo.Notification (
    NotificationId uniqueidentifier NOT NULL CONSTRAINT PK_Notification PRIMARY KEY,
    RestaurantId uniqueidentifier NOT NULL,
    EventKey nvarchar(200) NOT NULL,
    Type nvarchar(80) NOT NULL,
    Title nvarchar(200) NOT NULL,
    Message nvarchar(2000) NOT NULL,
    EntityType nvarchar(40) NOT NULL,
    EntityId uniqueidentifier NOT NULL,
    CreatedAt datetime2 NOT NULL,
    Suppressed bit NOT NULL CONSTRAINT DF_Notification_Suppressed DEFAULT (0)
);
CREATE UNIQUE INDEX IX_Notification_EventKey ON dbo.Notification(EventKey);
CREATE INDEX IX_Notification_RestaurantId_CreatedAt ON dbo.Notification(RestaurantId, CreatedAt);
CREATE TABLE dbo.NotificationRecipient (
    NotificationId uniqueidentifier NOT NULL,
    UserId uniqueidentifier NOT NULL,
    IsCustomer bit NOT NULL,
    ReadAt datetime2 NULL,
    CONSTRAINT PK_NotificationRecipient PRIMARY KEY (NotificationId, UserId),
    CONSTRAINT FK_NotificationRecipient_Notification FOREIGN KEY (NotificationId) REFERENCES dbo.Notification(NotificationId) ON DELETE CASCADE
);
CREATE INDEX IX_NotificationRecipient_UserId_ReadAt ON dbo.NotificationRecipient(UserId, ReadAt);
CREATE TABLE dbo.Announcement (
    AnnouncementId uniqueidentifier NOT NULL CONSTRAINT PK_Announcement PRIMARY KEY,
    RestaurantId uniqueidentifier NOT NULL,
    CreatedBy uniqueidentifier NOT NULL,
    Title nvarchar(200) NOT NULL,
    Message nvarchar(2000) NOT NULL,
    AllStaff bit NOT NULL,
    RecipientRoles nvarchar(200) NOT NULL,
    Version uniqueidentifier NOT NULL,
    CreatedAt datetime2 NOT NULL,
    SentAt datetime2 NULL,
    NotificationId uniqueidentifier NULL
);
CREATE INDEX IX_Announcement_RestaurantId_CreatedAt ON dbo.Announcement(RestaurantId, CreatedAt);
CREATE TABLE dbo.NotificationSetting (
    RestaurantId uniqueidentifier NOT NULL,
    Type nvarchar(80) NOT NULL,
    Enabled bit NOT NULL,
    CONSTRAINT PK_NotificationSetting PRIMARY KEY (RestaurantId, Type)
);
GO

CREATE TABLE dbo.StaffTransfer (
    StaffTransferId uniqueidentifier NOT NULL CONSTRAINT PK_StaffTransfer PRIMARY KEY,
    UserId uniqueidentifier NOT NULL,
    SourceRestaurantId uniqueidentifier NOT NULL,
    TargetRestaurantId uniqueidentifier NOT NULL,
    RequestedBy uniqueidentifier NOT NULL,
    WasActive bit NOT NULL,
    CompletedAt datetime2 NULL,
    Status nvarchar(20) NOT NULL,
    Message nvarchar(500) NULL,
    CreatedAt datetime2 NOT NULL
);
GO
CREATE INDEX IX_StaffTransfer_UserId_Status ON dbo.StaffTransfer(UserId, Status);
GO
