SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.StaffTransferReceipt', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StaffTransferReceipt (
        StaffTransferReceiptId uniqueidentifier NOT NULL CONSTRAINT PK_StaffTransferReceipt PRIMARY KEY,
        UserId uniqueidentifier NOT NULL,
        SourceRestaurantId uniqueidentifier NOT NULL,
        TargetRestaurantId uniqueidentifier NOT NULL,
        Status nvarchar(20) NOT NULL,
        Message nvarchar(500) NULL,
        CreatedAt datetime2 NOT NULL
    );
END;

COMMIT TRANSACTION;
GO
