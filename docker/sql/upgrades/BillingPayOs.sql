SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @lockResult int;
EXEC @lockResult=sys.sp_getapplock @Resource=N'Billing.PayOS.Schema',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
IF @lockResult < 0 THROW 51000, 'PayOS schema upgrade is busy.', 1;
IF OBJECT_ID(N'dbo.Payment',N'U') IS NULL OR OBJECT_ID(N'dbo.PaymentMethod',N'U') IS NULL
    THROW 51000, 'Existing Payment and PaymentMethod tables are required.', 1;
IF COL_LENGTH(N'dbo.Payment',N'ProviderPaymentLinkId') IS NULL
    ALTER TABLE dbo.Payment ADD ProviderPaymentLinkId varchar(100) NULL;
IF COL_LENGTH(N'dbo.Payment',N'ProviderCheckoutUrl') IS NULL
    ALTER TABLE dbo.Payment ADD ProviderCheckoutUrl varchar(2048) NULL;
IF COL_LENGTH(N'dbo.Payment',N'ProviderQrCode') IS NULL
    ALTER TABLE dbo.Payment ADD ProviderQrCode varchar(max) NULL;
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Payment') AND name IN(N'ProviderPaymentLinkId',N'ProviderCheckoutUrl',N'ProviderQrCode') AND (system_type_id<>167 OR is_nullable<>1 OR default_object_id<>0 OR max_length<>CASE name WHEN N'ProviderPaymentLinkId' THEN 100 WHEN N'ProviderCheckoutUrl' THEN 2048 ELSE -1 END))
    THROW 51000, 'PayOS metadata columns have unexpected types, nullability or defaults.', 1;
IF NOT EXISTS(SELECT 1 FROM dbo.PaymentMethod WITH(UPDLOCK,HOLDLOCK) WHERE MethodCode='BankTransfer')
    INSERT dbo.PaymentMethod(MethodCode,IsEnabled) VALUES('BankTransfer',1);
COMMIT TRANSACTION;
