SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @LockResult int;
    EXEC @LockResult = sys.sp_getapplock @Resource = N'Billing.ProviderConfiguration.Schema',
        @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 30000;
    IF @LockResult < 0
        THROW 51000, 'Cannot acquire the Billing provider configuration upgrade lock.', 1;
    IF OBJECT_ID(N'dbo.PaymentProviderConfig', N'U') IS NULL
        THROW 51000, 'PaymentProviderConfig is missing. Review the BillingDb baseline before upgrading.', 1;

    -- Required values belong to the restaurant owner. Never invent URLs or enable a channel.
    IF EXISTS (SELECT 1 FROM dbo.PaymentProviderConfig WITH (TABLOCKX, HOLDLOCK))
        AND (COL_LENGTH(N'dbo.PaymentProviderConfig', N'Enabled') IS NULL
            OR COL_LENGTH(N'dbo.PaymentProviderConfig', N'ReturnUrl') IS NULL
            OR COL_LENGTH(N'dbo.PaymentProviderConfig', N'CancelUrl') IS NULL
            OR COL_LENGTH(N'dbo.PaymentProviderConfig', N'RestaurantId') IS NULL)
        THROW 51000, 'Existing PaymentProviderConfig rows need explicit restaurant, Enabled, ReturnUrl and CancelUrl values. No configuration was guessed or deleted.', 1;

    IF COL_LENGTH(N'dbo.PaymentProviderConfig', N'Enabled') IS NULL
        ALTER TABLE dbo.PaymentProviderConfig ADD Enabled bit NOT NULL;
    IF COL_LENGTH(N'dbo.PaymentProviderConfig', N'ReturnUrl') IS NULL
        ALTER TABLE dbo.PaymentProviderConfig ADD ReturnUrl varchar(2048) NOT NULL;
    IF COL_LENGTH(N'dbo.PaymentProviderConfig', N'CancelUrl') IS NULL
        ALTER TABLE dbo.PaymentProviderConfig ADD CancelUrl varchar(2048) NOT NULL;
    IF COL_LENGTH(N'dbo.PaymentProviderConfig', N'RestaurantId') IS NULL
        ALTER TABLE dbo.PaymentProviderConfig ADD RestaurantId uniqueidentifier NOT NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.PaymentProviderConfig')
        AND name = N'RestaurantId' AND system_type_id = 36 AND is_computed = 0)
        THROW 51000, 'PaymentProviderConfig.RestaurantId must be a uniqueidentifier.', 1;
    EXEC sys.sp_executesql N'
        IF EXISTS (SELECT 1 FROM dbo.PaymentProviderConfig WHERE RestaurantId IS NULL
            OR RestaurantId = ''00000000-0000-0000-0000-000000000000'')
            THROW 51000, ''Existing PaymentProviderConfig rows require explicit valid restaurant mappings.'', 1;';
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.PaymentProviderConfig')
        AND name = N'RestaurantId' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes AS i
            JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(N'dbo.PaymentProviderConfig') AND c.name = N'RestaurantId'
                AND i.name NOT IN (N'IX_PaymentProviderConfig_RestaurantIsolation', N'IX_PaymentProviderConfig_RestaurantId_Provider'))
            THROW 51000, 'Custom PaymentProviderConfig restaurant indexes require review before changing nullability.', 1;
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PaymentProviderConfig')
            AND name = N'IX_PaymentProviderConfig_RestaurantIsolation')
            DROP INDEX IX_PaymentProviderConfig_RestaurantIsolation ON dbo.PaymentProviderConfig;
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PaymentProviderConfig')
            AND name = N'IX_PaymentProviderConfig_RestaurantId_Provider')
            DROP INDEX IX_PaymentProviderConfig_RestaurantId_Provider ON dbo.PaymentProviderConfig;
        ALTER TABLE dbo.PaymentProviderConfig ALTER COLUMN RestaurantId uniqueidentifier NOT NULL;
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.PaymentProviderConfig')
        AND name = N'SecretReference' AND system_type_id = 167 AND is_nullable = 0
        AND is_computed = 0 AND max_length IN (500, -1))
        THROW 51000, 'Unexpected PaymentProviderConfig.SecretReference definition. Review before upgrading.', 1;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.PaymentProviderConfig')
        AND name = N'SecretReference' AND max_length = 500)
        ALTER TABLE dbo.PaymentProviderConfig ALTER COLUMN SecretReference varchar(max) NOT NULL;

    IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.PaymentProviderConfig')
        AND is_nullable = 0 AND is_computed = 0 AND default_object_id = 0
        AND ((name = N'Enabled' AND system_type_id = 104)
            OR (name IN (N'ReturnUrl', N'CancelUrl') AND system_type_id = 167 AND max_length = 2048))) <> 3
        THROW 51000, 'Unexpected Billing provider configuration column definitions.', 1;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PaymentProviderConfig')
        AND name = N'IX_PaymentProviderConfig_RestaurantId_Provider')
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX IX_PaymentProviderConfig_RestaurantId_Provider
            ON dbo.PaymentProviderConfig(RestaurantId, Provider);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PaymentProviderConfig')
        AND name = N'IX_PaymentProviderConfig_RestaurantIsolation')
        EXEC sys.sp_executesql N'CREATE INDEX IX_PaymentProviderConfig_RestaurantIsolation
            ON dbo.PaymentProviderConfig(RestaurantId);';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
