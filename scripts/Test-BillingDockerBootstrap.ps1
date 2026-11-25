$ErrorActionPreference = 'Stop'
$suffix = [Guid]::NewGuid().ToString('N').Substring(0,10)
$tag = 'backend-billing-bootstrap-test:' + $suffix
$network = 'billing-bootstrap-test-' + $suffix
$server = 'billing-bootstrap-sql-' + $suffix
$oldPassword = $env:MSSQL_SA_PASSWORD
$oldSqlPassword = $env:SQLCMDPASSWORD
$env:MSSQL_SA_PASSWORD = 'Aa1!' + [Guid]::NewGuid().ToString('N')
$env:SQLCMDPASSWORD = $env:MSSQL_SA_PASSWORD
function Assert-Exit([string]$step) {
    if ($LASTEXITCODE -ne 0) { throw "$step failed (exit $LASTEXITCODE)." }
}
function Query([string]$sql) {
    $result = $sql | docker exec -i $server bash -lc 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d BillingDb -W'
    $result | Write-Output
    Assert-Exit 'SQL assertion'
}
function Initialize {
    docker run --rm --network $network --env SQLCMDPASSWORD $tag
    Assert-Exit 'Docker initializer'
}

function Expect-InitializerFailure([string]$message) {
    $errorLog = docker run --rm --network $network --env SQLCMDPASSWORD $tag 2>&1
    if ($LASTEXITCODE -eq 0) { throw 'Initializer unexpectedly accepted an incompatible database.' }
    if (($errorLog -join "`n") -notmatch [regex]::Escape($message)) { throw 'Unexpected initializer failure.' }
}
try {
    $sqlDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'docker/sql'
    docker build -t $tag $sqlDirectory
    Assert-Exit 'Build initializer from current source'
    docker network create $network | Out-Null
    Assert-Exit 'Create test network'
    docker run -d --name $server --network $network --network-alias sqlserver --env ACCEPT_EULA=Y --env MSSQL_SA_PASSWORD mcr.microsoft.com/mssql/server:2022-latest | Out-Null
    Assert-Exit 'Start isolated SQL'
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        docker exec $server bash -lc 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -Q "SELECT 1" -o /dev/null' 2>$null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Seconds 2
    }
    if (-not $ready) { throw 'Isolated SQL not ready.' }
    Initialize
    Query "SET NOCOUNT ON; IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.BillCheckout') AND name IN ('CashRegisterId','SubmittedAt','PreparedByRef','PreparedAt','PresentedAt'))<>5 THROW 51000, 'Missing columns on fresh bootstrap.',1; PRINT 'PASS fresh Docker bootstrap';"
    Query @"
SET NOCOUNT ON;
SELECT CAST('BillingDb' AS nvarchar(128)) AS DatabaseName,t.name AS TableName,c.name AS ColumnName,c.system_type_id,c.max_length,c.precision,c.scale,c.is_nullable
INTO tempdb.dbo.ExpectedBillingAiSchema
FROM sys.tables t JOIN sys.columns c ON t.object_id=c.object_id;
INSERT tempdb.dbo.ExpectedBillingAiSchema
SELECT 'OrderingDb',t.name,c.name,c.system_type_id,c.max_length,c.precision,c.scale,c.is_nullable
FROM OrderingDb.sys.tables t JOIN OrderingDb.sys.columns c ON t.object_id=c.object_id;
ALTER TABLE dbo.Payment DROP COLUMN ProviderPaymentLinkId, ProviderCheckoutUrl, ProviderQrCode;
DELETE dbo.PaymentMethod WHERE MethodCode='BankTransfer';
UPDATE dbo.PaymentMethod SET IsEnabled=0 WHERE MethodCode='Cash';
DROP INDEX IX_CashHandover_RestaurantIsolation ON dbo.CashHandover;
ALTER TABLE dbo.CashHandover DROP COLUMN RestaurantId;
DROP INDEX IX_PaymentProviderConfig_RestaurantId_Provider ON dbo.PaymentProviderConfig;
ALTER TABLE dbo.PaymentProviderConfig DROP COLUMN Enabled, ReturnUrl, CancelUrl;
ALTER TABLE dbo.PaymentProviderConfig ALTER COLUMN RestaurantId uniqueidentifier NULL;
ALTER TABLE dbo.PaymentProviderConfig ALTER COLUMN SecretReference varchar(500) NOT NULL;
ALTER TABLE dbo.BillLine ALTER COLUMN CogsSnapshot decimal(18,2) NOT NULL;
DROP TABLE dbo.NotificationOutbox;
USE OrderingDb;
DROP TABLE dbo.NotificationOutbox;
ALTER TABLE dbo.[Order] DROP COLUMN RefundBankName, RefundAccountNumber, RefundAccountHolder;
USE RestaurantDb;
ALTER TABLE dbo.Qr DROP COLUMN ImageUrl;
"@
    Query "SET NOCOUNT ON; ALTER TABLE dbo.BillCheckout DROP CONSTRAINT FK_BillCheckout_CashRegister; ALTER TABLE dbo.BillCheckout DROP COLUMN CashRegisterId, SubmittedAt, PreparedByRef, PreparedAt, PresentedAt; INSERT dbo.BillCheckout(CheckoutId,TableSessionRef,Status,CreatedAt) VALUES('10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000002','Frozen','2026-11-01T00:00:00');"
    'SELECT TOP(0) CashRegisterId, SubmittedAt, PreparedByRef, PreparedAt, PresentedAt FROM dbo.BillCheckout;' | docker exec -i $server bash -lc 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d BillingDb' 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { throw 'Legacy missing-column reproduction did not fail.' }
    Write-Output 'PASS reproduced invalid-column failure on isolated legacy database'
    Initialize
    Initialize
    Query @"
SET NOCOUNT ON;
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.BillCheckout') AND name IN ('CashRegisterId','SubmittedAt','PreparedByRef','PreparedAt','PresentedAt') AND is_nullable=1 AND default_object_id=0)<>5 THROW 51000, 'Upgrade columns incorrect.',1;
IF (SELECT COUNT(*) FROM dbo.BillCheckout)<>1 THROW 51000, 'Existing checkout count changed.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.BillCheckout WHERE CheckoutId='10000000-0000-0000-0000-000000000001' AND TableSessionRef='10000000-0000-0000-0000-000000000002' AND Status='Frozen' AND CreatedAt='2026-11-01T00:00:00' AND CashRegisterId IS NULL AND SubmittedAt IS NULL AND PreparedByRef IS NULL AND PreparedAt IS NULL AND PresentedAt IS NULL) THROW 51000, 'Existing checkout changed.',1;
SELECT TOP(0) CashRegisterId, SubmittedAt, PreparedByRef, PreparedAt, PresentedAt FROM dbo.BillCheckout;
PRINT 'PASS legacy Docker upgrade, repeat run, preserved checkout and NULL metadata';
IF EXISTS (SELECT * FROM tempdb.dbo.ExpectedBillingAiSchema WHERE DatabaseName='BillingDb'
    EXCEPT SELECT 'BillingDb',t.name,c.name,c.system_type_id,c.max_length,c.precision,c.scale,c.is_nullable
    FROM sys.tables t JOIN sys.columns c ON t.object_id=c.object_id)
    THROW 51000, 'Billing schema does not match fresh bootstrap after upgrades.',1;
IF EXISTS (SELECT * FROM tempdb.dbo.ExpectedBillingAiSchema WHERE DatabaseName='OrderingDb'
    EXCEPT SELECT 'OrderingDb',t.name,c.name,c.system_type_id,c.max_length,c.precision,c.scale,c.is_nullable
    FROM OrderingDb.sys.tables t JOIN OrderingDb.sys.columns c ON t.object_id=c.object_id)
    THROW 51000, 'Ordering schema does not match fresh bootstrap after upgrades.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.PaymentMethod WHERE MethodCode='Cash' AND IsEnabled=0)
    THROW 51000, 'Disabled Cash method was re-enabled.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.PaymentMethod WHERE MethodCode='BankTransfer' AND IsEnabled=1)
    THROW 51000, 'BankTransfer method was not seeded.',1;
PRINT 'PASS all Billing/Ordering columns match fresh schema; seed preserves disabled methods';
IF COL_LENGTH('RestaurantDb.dbo.Qr','ImageUrl') IS NULL
    THROW 51000, 'QR image column was not upgraded.',1;
"@
    Query "DROP TABLE dbo.BillCheckout; USE OrderingDb; DROP TABLE dbo.CheckoutLock;"
    Initialize
    Query "IF OBJECT_ID('dbo.BillCheckout','U') IS NULL OR OBJECT_ID('OrderingDb.dbo.CheckoutLock','U') IS NULL THROW 51000, 'Missing checkout tables were not created.',1; IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.BillCheckout') AND name='UX_BillCheckout_ActiveSession' AND is_unique=1 AND has_filter=1) THROW 51000, 'Missing checkout session uniqueness.',1; PRINT 'PASS missing checkout tables are created with active-session uniqueness';"
    Query "SET NOCOUNT ON; DROP INDEX UX_CashRegister_Restaurant ON dbo.CashRegister; INSERT dbo.CashRegister(CashRegisterId,RestaurantId,Code) VALUES(NEWID(),'10000000-0000-0000-0000-000000000003','OLD-A'),(NEWID(),'10000000-0000-0000-0000-000000000003','OLD-B');"
    Expect-InitializerFailure 'Multiple cash registers'
    Query "SET NOCOUNT ON; IF (SELECT COUNT(*) FROM dbo.CashRegister)<>2 THROW 51000, 'Duplicate register rows changed.',1; PRINT 'PASS duplicate registers block initializer without deleting data';"
    Query "DELETE dbo.CashRegister WHERE Code='OLD-B'; INSERT dbo.PaymentProviderConfig(PaymentProviderConfigId,RestaurantId,Provider,ClientId,SecretReference,WebhookUrl,IsVerified,Enabled,ReturnUrl,CancelUrl) VALUES(NEWID(),'10000000-0000-0000-0000-000000000003','PayOS','test-client','synthetic-not-a-secret','https://example.test/webhook',0,0,'https://example.test/return','https://example.test/cancel'); ALTER TABLE dbo.PaymentProviderConfig DROP COLUMN Enabled;"
    Expect-InitializerFailure 'Existing PaymentProviderConfig rows need explicit'
    Query "IF COL_LENGTH('dbo.PaymentProviderConfig','Enabled') IS NOT NULL OR (SELECT COUNT(*) FROM dbo.PaymentProviderConfig)<>1 THROW 51000, 'Legacy provider was modified despite missing required data.',1; PRINT 'PASS provider with missing required values blocks without guessing or deleting';"
    Query "ALTER TABLE dbo.PaymentProviderConfig ADD Enabled bit NULL; EXEC(N'UPDATE dbo.PaymentProviderConfig SET Enabled=0'); ALTER TABLE dbo.PaymentProviderConfig ALTER COLUMN Enabled bit NOT NULL;"
    Query "USE CatalogDb; ALTER TABLE dbo.Item DROP COLUMN PreparationTimeMinutes;"
    Expect-InitializerFailure 'PreparationTimeMinutes'
    Write-Output 'PASS unexpected missing AI dependency column blocks bootstrap before services start'
    Query "USE CatalogDb; ALTER TABLE dbo.Item ADD PreparationTimeMinutes int NULL; USE IdentityDb; ALTER TABLE dbo.[User] DROP CONSTRAINT DF_User_ProvisioningPending; ALTER TABLE dbo.[User] DROP COLUMN ProvisioningPending;"
    Expect-InitializerFailure 'ProvisioningPending'
    Write-Output 'PASS outdated Identity dependency blocks bootstrap with a schema error'
    Write-Output 'ALL DOCKER BOOTSTRAP CHECKS PASSED'
}
finally {
    docker rm -fv $server 2>$null | Out-Null
    docker network rm $network 2>$null | Out-Null
    docker image rm $tag 2>$null | Out-Null
    $env:MSSQL_SA_PASSWORD = $oldPassword
    $env:SQLCMDPASSWORD = $oldSqlPassword
}


