-- Existing reservations retain their original area; new Pending rows may be unassigned.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.Reservation', N'U') IS NULL
        THROW 51000, 'Reservation table is missing; initialize RestaurantDb before this upgrade.', 1;
    IF NOT EXISTS
    (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.Reservation')
            AND name = N'RequestedAreaId' AND system_type_id = 36
    )
        THROW 51000, 'Reservation.RequestedAreaId must exist with type uniqueidentifier.', 1;
    IF EXISTS
    (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.Reservation')
            AND name = N'RequestedAreaId' AND is_nullable = 0
    )
        ALTER TABLE dbo.Reservation ALTER COLUMN RequestedAreaId uniqueidentifier NULL;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
