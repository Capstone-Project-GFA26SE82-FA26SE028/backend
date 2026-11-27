SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
IF OBJECT_ID(N'dbo.RestaurantRegistration', N'U') IS NULL
BEGIN
    BEGIN TRANSACTION;
    CREATE TABLE dbo.[RestaurantRegistration] (
        [RegistrationId] uniqueidentifier NOT NULL,
        [ApplicantUserRef] uniqueidentifier NOT NULL,
        [NewAccount] bit NOT NULL,
        [Email] nvarchar(255) NOT NULL,
        [RestaurantDetails] nvarchar(max) NOT NULL,
        [BusinessCertificateAssetId] varchar(100) NOT NULL,
        [FoodSafetyCertificateAssetId] varchar(100) NOT NULL,
        [Status] varchar(20) NOT NULL,
        [SubmittedAt] datetime2(7) NOT NULL,
        [ReviewedAt] datetime2(7) NULL,
        [ReviewedByRef] uniqueidentifier NULL,
        [RestaurantId] uniqueidentifier NULL
    );
    ALTER TABLE dbo.[RestaurantRegistration] ADD CONSTRAINT [PK_RestaurantRegistration] PRIMARY KEY ([RegistrationId]);
    ALTER TABLE dbo.[RestaurantRegistration] ADD CONSTRAINT [FK_RestaurantRegistration_Restaurant] FOREIGN KEY ([RestaurantId]) REFERENCES dbo.[Restaurant] ([RestaurantId]);
    ALTER TABLE dbo.[RestaurantRegistration] ADD CONSTRAINT [CK_RestaurantRegistration_Status] CHECK (
        ([Status] = 'Pending' AND [RestaurantId] IS NULL AND [ReviewedByRef] IS NULL AND [ReviewedAt] IS NULL)
        OR ([Status] IN ('Provisioning', 'Approved') AND [RestaurantId] IS NOT NULL AND [ReviewedByRef] IS NOT NULL AND [ReviewedAt] IS NOT NULL));
    ALTER TABLE dbo.[RestaurantRegistration] ADD CONSTRAINT [CK_RestaurantRegistration_Details] CHECK (ISJSON([RestaurantDetails]) = 1);
    CREATE UNIQUE INDEX [UX_RestaurantRegistration_Applicant] ON dbo.[RestaurantRegistration] ([ApplicantUserRef]);
    CREATE UNIQUE INDEX [UX_RestaurantRegistration_Restaurant] ON dbo.[RestaurantRegistration] ([RestaurantId]) WHERE [RestaurantId] IS NOT NULL;
    CREATE INDEX [IX_RestaurantRegistration_Review] ON dbo.[RestaurantRegistration] ([Status], [SubmittedAt] DESC, [RegistrationId]);
    COMMIT TRANSACTION;
END;
GO
