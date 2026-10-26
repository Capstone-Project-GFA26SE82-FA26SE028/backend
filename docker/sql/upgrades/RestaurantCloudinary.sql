IF COL_LENGTH('dbo.Qr', 'ImageUrl') IS NULL
BEGIN
    ALTER TABLE dbo.Qr ADD ImageUrl varchar(2048) NULL;
END;
GO
