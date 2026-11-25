#requires -Version 5.1
[CmdletBinding()]
param([string]$OutputDirectory = (Join-Path ([IO.Path]::GetTempPath()) 'restaurant-report-results'))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$utf8NoBom = [Text.UTF8Encoding]::new($false)
$OutputEncoding = $utf8NoBom
try { [Console]::OutputEncoding = $utf8NoBom } catch {}
$httpClient = [System.Net.Http.HttpClient]::new()
$httpClient.Timeout = [TimeSpan]::FromSeconds(45)
$root = Split-Path $PSScriptRoot -Parent
$fixtures = Join-Path $root 'Tests/Fixtures/RestaurantReports'
$project = 'report-r6-' + [Guid]::NewGuid().ToString('N').Substring(0, 10)
$privateDirectory = Join-Path ([IO.Path]::GetTempPath()) $project
$envFile = Join-Path $privateDirectory 'docker.env'
$override = Join-Path $privateDirectory 'compose.test.yaml'
$server = "$project-sqlserver-1"
$checks = [Collections.Generic.List[object]]::new()
$cases = [ordered]@{}
1..18 | ForEach-Object { $cases[('TC-WF05-{0:00}' -f $_)] = @{ execution = 'NotRun'; acceptance = 'NotAssessed' } }
$cases['E2E-WF05-01'] = @{ execution = 'NotRun'; acceptance = 'NotAssessed' }
$expected = [IO.File]::ReadAllText((Join-Path $fixtures 'expected.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json
$a = '20000000-0000-0000-0000-000000000001'
$b = '20000000-0000-0000-0000-000000000099'
$itemA = '87000000-0000-0000-0000-000000000001'
$itemOnline = '87000000-0000-0000-0000-000000000002'
$itemGift = '87000000-0000-0000-0000-000000000003'
$itemPaid = '87000000-0000-0000-0000-000000000004'
$shiftOne = '82000000-0000-0000-0000-000000000001'
$shiftTwo = '82000000-0000-0000-0000-000000000002'
$period = 'from=2026-11-25T00:00:00%2B07:00&to=2026-11-26T00:00:00%2B07:00'
$nextPeriod = 'from=2026-11-26T00:00:00%2B07:00&to=2026-11-27T00:00:00%2B07:00'
$baseUrl = ''
$token = ''
$failure = $null
$cleanupSucceeded = $false
$originalEnvironment = @{}

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Amount($Actual, [decimal]$Wanted, [string]$Message) {
    Assert-True ($null -ne $Actual -and [decimal]$Actual -eq $Wanted) $Message
}

function Invoke-Native([string]$Command, [string[]]$Arguments, [string]$LogPath = '') {
    $executable = (Get-Command $Command -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $previousEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $global:LASTEXITCODE = $null
        if ($LogPath) {
            & $executable @Arguments *> $LogPath
            $exitCode = if ($null -eq $global:LASTEXITCODE) { 1 } else { $global:LASTEXITCODE }
            return @{ ExitCode = $exitCode; Output = @() }
        }
        $output = & $executable @Arguments 2>$null
        $exitCode = if ($null -eq $global:LASTEXITCODE) { 1 } else { $global:LASTEXITCODE }
        return @{ ExitCode = $exitCode; Output = @($output) }
    }
    finally {
        $ErrorActionPreference = $previousEap
    }
}

function Invoke-Compose([string[]]$Arguments, [string]$LogName) {
    $run = Invoke-Native 'docker' ($compose + $Arguments) (Join-Path $privateDirectory $LogName)
    Assert-True ($run.ExitCode -eq 0) "Compose failed: $LogName (private log retained; credentials not printed)."
}

function Assert-TestServer {
    $run = Invoke-Native 'docker' @('inspect', '--format', '{{index .Config.Labels `com.docker.compose.project`}}', $server)
    $label = ($run.Output -join '').Trim()
    Assert-True ($run.ExitCode -eq 0 -and $label -eq $project -and $project -match '^report-r6-[a-f0-9]{10}$') 'Refusing a database outside the isolated R6 project.'
}

function Invoke-Sql([string]$Database, [string]$Sql) {
    Assert-TestServer
    Assert-True ($Database -in @('BillingDb', 'OrderingDb', 'RestaurantDb')) 'Unexpected fixture database.'
    $encodedSql = [Convert]::ToBase64String($utf8NoBom.GetBytes($Sql))
    $run = Invoke-Native 'docker' @('exec', $server, 'bash', '-c', 'printf %s $2 | base64 -d | SQLCMDPASSWORD=$MSSQL_SA_PASSWORD /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -f 65001 -h -1 -W -d $1', 'bash', $Database, $encodedSql)
    Assert-True ($run.ExitCode -eq 0) "Test SQL failed in $Database."
    return ($run.Output | Where-Object { "$_" -and "$_".Trim() }) -join "`n"
}

function Invoke-Api([string]$Path, [string]$AccessToken, [int]$Status = 200, [string]$RestaurantHeader = '') {
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, "$baseUrl$Path")
    try {
        if ($AccessToken) { $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $AccessToken) }
        if ($RestaurantHeader) { $null = $request.Headers.TryAddWithoutValidation('X-Restaurant-Id', $RestaurantHeader) }
        $response = $httpClient.SendAsync($request).GetAwaiter().GetResult()
        try {
            $actualStatus = [int]$response.StatusCode
            Assert-True ($actualStatus -eq $Status) "Unexpected HTTP status for $Path; expected $Status, actual $actualStatus."
            if ($Status -eq 200) {
                $cacheControl = if ($null -ne $response.Headers.CacheControl) { $response.Headers.CacheControl.ToString() } else { '' }
                Assert-True ($cacheControl -match 'no-store') "Missing no-store Cache-Control header for $Path."
            }
            $bodyText = $utf8NoBom.GetString($response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult())
            $json = $bodyText | ConvertFrom-Json
            if ($Status -ne 200) { Assert-True ($null -eq $json.result) 'An error response exposed report data.' }
            $checks.Add(@{ path = $Path; expectedStatus = $Status; actualStatus = $actualStatus })
            return $json.result
        }
        finally { $response.Dispose() }
    }
    finally { $request.Dispose() }
}

function Get-Report([string]$Kind = 'sales', [string]$Filter = '', [string]$Range = $period, [string]$Owner = $a) {
    return Invoke-Api "/api/restaurants/$Owner/reports/${Kind}?$Range$Filter" $token
}

function Invoke-Case([string]$Id, [scriptblock]$Body, [string]$Acceptance = 'Covered', [string]$Limit = '') {
    try {
        $null = & $Body
        $cases[$Id] = @{ execution = 'Passed'; acceptance = $Acceptance; limitation = $Limit }
        Write-Host "$Id execution=Passed acceptance=$Acceptance"
    }
    catch {
        $cases[$Id] = @{ execution = 'Failed'; acceptance = 'Failed'; limitation = $_.Exception.Message }
        throw
    }
}

function Login([string]$Email) {
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$baseUrl/api/auth/login")
    $payload = @{ email = $Email; password = '123' } | ConvertTo-Json -Compress
    $request.Content = [System.Net.Http.StringContent]::new($payload, $utf8NoBom, 'application/json')
    $timeout = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(10))
    try {
        $response = $httpClient.SendAsync($request, $timeout.Token).GetAwaiter().GetResult()
        try {
            Assert-True ([int]$response.StatusCode -eq 200) 'Synthetic-account login failed.'
            $bodyText = $utf8NoBom.GetString($response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult())
            $checks.Add(@{ path = '/api/auth/login'; expectedStatus = 200; actualStatus = 200 })
            return ($bodyText | ConvertFrom-Json).result.accessToken
        }
        finally { $response.Dispose() }
    }
    finally {
        $request.Dispose()
        $timeout.Dispose()
    }
}

function Get-FinancialFingerprint {
    $billingState = Invoke-Sql BillingDb @"
SET NOCOUNT ON;
SELECT 'Bill',CONVERT(varchar(36),BillId),Status,SubTotal,DiscountAmount,TotalAmount FROM dbo.Bill ORDER BY BillId;
SELECT 'Payment',CONVERT(varchar(36),PaymentId),Status,Amount,PaidAt FROM dbo.Payment ORDER BY PaymentId;
SELECT 'Handover',CONVERT(varchar(36),PaymentId),Status,ConfirmedAmount FROM dbo.CashHandover ORDER BY PaymentId;
SELECT 'Line',CONVERT(varchar(36),BillLineId),Quantity,UnitPriceSnapshot,DiscountAllocated,CogsSnapshot FROM dbo.BillLine ORDER BY BillLineId;
SELECT 'Movement',CONVERT(varchar(36),CashMovementId),Type,SignedAmount FROM dbo.CashMovement ORDER BY CashMovementId;
SELECT 'Online',CONVERT(varchar(36),OrderId),Status,Amount,PaidAt,RefundedAt,TransactionReference FROM dbo.OnlinePayments ORDER BY OrderId;
"@
    $orderingState = Invoke-Sql OrderingDb @"
SET NOCOUNT ON;
SELECT 'Order',CONVERT(varchar(36),OrderId),Channel,Status,CONVERT(varchar(36),RestaurantRef) FROM dbo.[Order] ORDER BY OrderId;
SELECT 'OrderItem',CONVERT(varchar(36),OrderItemId),UnitPriceSnapshot,Quantity,CancelledQuantity,Status FROM dbo.OrderItem ORDER BY OrderItemId;
"@
    $restaurantState = Invoke-Sql RestaurantDb @"
SET NOCOUNT ON;
SELECT 'Staff',CONVERT(varchar(36),RestaurantId),CONVERT(varchar(36),UserIdRef),IsActive FROM dbo.RestaurantStaff ORDER BY RestaurantId,UserIdRef;
"@
    return ($billingState, $orderingState, $restaurantState) -join "`n"
}

try {
    $info = Invoke-Native 'docker' @('info', '--format', '{{.ServerVersion}}')
    Assert-True ($info.ExitCode -eq 0) 'Cannot connect to Docker Engine; start Docker Desktop with Linux containers.'
    New-Item -ItemType Directory -Path $privateDirectory | Out-Null
    $settings = @{
        SQL_SA_PASSWORD = 'Aa1!' + [Guid]::NewGuid().ToString('N')
        JWT_SECRET_KEY = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
        CLOUDINARY_API_KEY = 'isolated-no-upload'
        CLOUDINARY_API_SECRET = 'isolated-no-upload'
        GATEWAY_HTTP_PORT = '0'
    }
    foreach ($key in $settings.Keys) {
        $originalEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $settings[$key], 'Process')
    }
    $envLines = ($settings.GetEnumerator() | ForEach-Object { '{0}={1}' -f $_.Key, $_.Value }) -join "`n"
    [IO.File]::WriteAllText($envFile, $envLines + "`n", $utf8NoBom)
    $overrideYaml = @"
services:
  sqlserver:
    mem_limit: 2g
    environment:
      MSSQL_MEMORY_LIMIT_MB: '1024'
  db-init:
    image: ${project}-db-init:test
  identity:
    image: ${project}-identity:test
  restaurant:
    image: ${project}-restaurant:test
  billing:
    image: ${project}-billing:test
  ordering:
    image: ${project}-ordering:test
  gateway:
    image: ${project}-gateway:test
"@
    [IO.File]::WriteAllText($override, $overrideYaml, $utf8NoBom)
    $compose = @('compose', '--project-name', $project, '--env-file', $envFile, '-f', (Join-Path $root 'compose.yaml'), '-f', $override)
    Invoke-Compose @('build', 'db-init', 'identity', 'restaurant', 'billing', 'ordering', 'gateway') 'build.log'
    Invoke-Compose @('up', '-d', '--no-deps', '--wait', 'sqlserver') 'sql-start.log'
    Invoke-Compose @('run', '--rm', '--no-deps', 'db-init') 'init.log'
    Invoke-Compose @('run', '--rm', '--no-deps', '--entrypoint', '/bin/bash', 'db-init', '/init/seed.sh') 'seed.log'
    foreach ($database in @('RestaurantDb', 'OrderingDb', 'BillingDb')) {
        $null = Invoke-Sql $database ([IO.File]::ReadAllText((Join-Path $fixtures "$database.sql"), [Text.Encoding]::UTF8))
    }
    Invoke-Compose @('up', '-d', '--no-deps', 'identity', 'restaurant', 'billing', 'ordering', 'gateway') 'apps.log'
    $portInfo = Invoke-Native 'docker' @('port', "$project-gateway-1", '5000/tcp')
    $port = ($portInfo.Output | Select-Object -First 1) -split ':' | Select-Object -Last 1
    $baseUrl = 'http://127.0.0.1:' + "$port".Trim()
    $readiness = [Diagnostics.Stopwatch]::StartNew()
    for ($attempt = 0; $attempt -lt 30 -and $readiness.Elapsed.TotalSeconds -lt 120; $attempt++) {
        try { $token = Login 'manager@gmail.com'; break }
        catch { Start-Sleep -Seconds 1 }
    }
    Assert-True (-not [string]::IsNullOrWhiteSpace($token)) 'Gateway/Identity did not become ready.'
    foreach ($service in @('identity', 'restaurant', 'billing', 'ordering', 'gateway')) {
        $containerImage = Invoke-Native 'docker' @('inspect', '--format', '{{.Image}}', "$project-$service-1")
        $builtImage = Invoke-Native 'docker' @('image', 'inspect', '--format', '{{.Id}}', "$project-${service}:test")
        $actual = ($containerImage.Output -join '').Trim()
        $wanted = ($builtImage.Output -join '').Trim()
        Assert-True ($containerImage.ExitCode -eq 0 -and $builtImage.ExitCode -eq 0 -and $actual -match '^sha256:[a-f0-9]{64}$' -and $actual -eq $wanted) "Cannot verify the newly built image for $service."
    }
    $before = Get-FinancialFingerprint
    $sales = Get-Report
    $dishes = Get-Report 'dishes'
    Assert-True ($before -ceq (Get-FinancialFingerprint)) 'Initial report queries changed the fingerprinted source fields.'
    Invoke-Case 'TC-WF05-01' {
        $normalizedPeriod = Invoke-Api "/api/restaurants/$a/reports/period?$period&groupBy=week&timeZone=UTC" $token
        Assert-True ($normalizedPeriod.timeZone -eq 'Asia/Ho_Chi_Minh' -and $normalizedPeriod.weekStartsOn -eq 'Monday') 'Period endpoint accepted client time zone or wrong week start.'
        foreach ($group in @('hour', 'day', 'week', 'month')) {
            $report = Get-Report 'sales' "&groupBy=$group&timeZone=UTC"
            Assert-Amount $report.totals.collections.amount $expected.collections 'Grouping changed collections.'
            Assert-Amount (($report.groups | ForEach-Object { [decimal]$_.totals.collections.amount } | Measure-Object -Sum).Sum) $expected.collections 'Grouped totals do not reconcile.'
            Assert-True ($report.period.timeZone -eq 'Asia/Ho_Chi_Minh') 'Client/server zone replaced restaurant time zone.'
            Assert-True ([DateTimeOffset]$report.period.fromUtc -eq [DateTimeOffset]'2026-11-24T17:00:00Z' -and [DateTimeOffset]$report.period.toUtc -eq [DateTimeOffset]'2026-11-25T17:00:00Z' -and $report.period.startInclusive -and $report.period.endExclusive) 'Incorrect half-open UTC range.'
            if ($group -eq 'week') { Assert-True ($report.period.weekStartsOn -eq 'Monday' -and $report.groups[0].key -eq '2026-11-23') 'Wrong week boundary.' }
        }
    }
    Invoke-Case 'TC-WF05-02' {
        foreach ($range in @('from=2026-11-25&to=2026-11-26', 'from=2026-11-25T00:00:00Z&to=2026-11-25T00:00:00Z', 'from=2026-11-26T00:00:00Z&to=2026-11-25T00:00:00Z', "$period&groupBy=quarter")) {
            $null = Invoke-Api "/api/restaurants/$a/reports/sales?$range" $token 400
        }
    }
    Invoke-Case 'TC-WF05-03' {
        $range = 'from=2026-10-01T00:00:00Z&to=2026-10-02T00:00:00Z'
        $empty = Get-Report 'sales' '' $range
        $emptyDishes = Get-Report 'dishes' '' $range
        Assert-Amount $empty.totals.collections.amount 0 'Empty period contains collections.'
        Assert-Amount $empty.totals.paidOrders.count 0 'Empty period contains paid orders.'
        Assert-True (@($empty.groups).Count -eq 0 -and @($emptyDishes.items).Count -eq 0 -and [bool]$emptyDishes.quantityComplete) 'Empty period contains rows or false dish gap.'
        Assert-True ($null -eq $empty.totals.refunds.amount -and $null -eq $empty.totals.netCollections.amount -and $null -eq $empty.totals.netDishRevenue.amount) 'Missing refund source treated as complete.'
    } 'Partial' 'Collections/lists covered; full empty-period refund and net-profit totals unsupported, not zero-filled.'
    Invoke-Case 'TC-WF05-04' {
        Assert-Amount $sales.totals.collections.amount $expected.collections 'Confirmed collections wrong; boundary or unverified attempts included.'
        Assert-Amount $sales.totals.paidOrders.count $expected.paidOrders 'Paid orders multiplied by lines.'
        $null = Invoke-Sql BillingDb "UPDATE dbo.CashHandover SET Status='Pending' WHERE PaymentId='84000000-0000-0000-0000-000000000001';"
        try {
            $invalid = Get-Report
            Assert-Amount $invalid.totals.collections.knownAmount $expected.bankTransfer 'Unconfirmed cash evidence accepted.'
            Assert-True ($null -eq $invalid.totals.collections.amount) 'Inconsistent paid cash presented as complete.'
        }
        finally { $null = Invoke-Sql BillingDb "UPDATE dbo.CashHandover SET Status='Confirmed' WHERE PaymentId='84000000-0000-0000-0000-000000000001';" }
    } 'Partial' 'Cash evidence transition is SQL fault injection, not an actual cashier-confirmation API call.'
    Invoke-Case 'TC-WF05-05' {
        Assert-Amount $sales.totals.refunds.knownAmount 0 'Refund moved to sale period.'
        foreach ($state in @('Paid', 'RefundPending')) {
            $null = Invoke-Sql BillingDb "UPDATE dbo.OnlinePayments SET Status='$state' WHERE ProviderOrderCode=20005;"
            try {
                Assert-Amount (Get-Report).totals.collections.amount $expected.collections 'A refund state removed the original verified collection.'
                Assert-Amount (Get-Report 'sales' '' $nextPeriod).totals.refunds.knownAmount 0 'An unverified/pending refund reduced collections.'
            }
            finally { $null = Invoke-Sql BillingDb "UPDATE dbo.OnlinePayments SET Status='Refunded' WHERE ProviderOrderCode=20005;" }
        }
        $next = Get-Report 'sales' '' $nextPeriod
        Assert-Amount $next.totals.collections.amount $expected.nextCollections 'End-boundary payment missing in adjacent period.'
        Assert-Amount $next.totals.refunds.knownAmount $expected.nextRefundsKnown 'Verified refund time wrong.'
        Assert-Amount $next.totals.netCollections.knownAmount $expected.nextNetCollectionsKnown 'Known net collections wrong.'
        Assert-True ($null -eq $next.totals.netCollections.amount) 'Unknown dine-in refunds hidden.'
    } 'Partial' 'Online refund covered; dine-in verification and refunded dish/VAT/fee breakdown unavailable.'
    Invoke-Case 'TC-WF05-06' {
        $repeat = Get-Report
        Assert-True (($sales.totals | ConvertTo-Json -Depth 12 -Compress) -ceq ($repeat.totals | ConvertTo-Json -Depth 12 -Compress)) 'Repeated query changed totals.'
        $null = Invoke-Sql BillingDb "UPDATE dbo.OnlinePayments SET TransactionReference='R6-TXN-2' WHERE ProviderOrderCode=20005;"
        try {
            $duplicate = Get-Report
            Assert-Amount $duplicate.totals.collections.knownAmount 254 'Duplicated provider transaction counted.'
            Assert-True ($null -eq $duplicate.totals.collections.amount) 'Duplicate presented as complete.'
        }
        finally { $null = Invoke-Sql BillingDb "UPDATE dbo.OnlinePayments SET TransactionReference='R6-ONLINE-TXN' WHERE ProviderOrderCode=20005;" }
    }
    Invoke-Case 'TC-WF05-07' {
        foreach ($channel in @('DineIn', 'Mixed', 'Takeaway')) {
            $row = $sales.channels | Where-Object key -eq $channel
            Assert-Amount $row.totals.collections.amount $expected.channels.$channel 'Channel partition wrong.'
            Assert-Amount (Get-Report 'sales' "&channel=$channel").totals.collections.amount $expected.channels.$channel 'Channel filter disagrees with partition.'
        }
    }
    Invoke-Case 'TC-WF05-08' {
        Assert-Amount (($sales.shifts | ForEach-Object { [decimal]$_.totals.collections.amount } | Measure-Object -Sum).Sum) $expected.collections 'Shift partition sum differs from collections.'
        foreach ($pair in @(@($shiftOne, $expected.shiftOne), @($shiftTwo, $expected.shiftTwo), @('unassigned', $expected.unassigned))) {
            Assert-Amount (Get-Report 'sales' "&shift=$($pair[0])").totals.collections.amount $pair[1] 'Shift filter wrong.'
        }
    }
    Invoke-Case 'TC-WF05-09' {
        Assert-Amount (($sales.methods | ForEach-Object { [decimal]$_.totals.collections.amount } | Measure-Object -Sum).Sum) $expected.collections 'Method totals differ from collections.'
        Assert-Amount (Get-Report 'sales' '&methodCode=Cash').totals.collections.amount $expected.cash 'Cash total wrong.'
        Assert-Amount (Get-Report 'sales' '&methodCode=BankTransfer').totals.collections.amount $expected.bankTransfer 'Bank total wrong.'
    }
    Invoke-Case 'TC-WF05-10' {
        $main = $dishes.items | Where-Object itemId -eq $itemA
        $onlineDish = $dishes.items | Where-Object itemId -eq $itemOnline
        Assert-True ($dishes.items[0].itemId -eq $itemA) 'Quantity ranking wrong.'
        Assert-Amount $main.quantity.quantity $expected.mainItemQuantity 'Cancellation subtracted twice.'
        Assert-Amount $onlineDish.quantity.quantity 1 'Takeaway partial cancellation wrong.'
        $null = Invoke-Sql OrderingDb "UPDATE dbo.[Order] SET Status='Rejected' WHERE OrderId='85000000-0000-0000-0000-000000000005';"
        try {
            Assert-True (@((Get-Report 'dishes' "&itemId=$itemOnline").items).Count -eq 0) 'Rejected order counted in dishes.'
            Assert-Amount (Get-Report).totals.collections.amount $expected.collections 'Order rejection erased verified cash flow.'
        }
        finally { $null = Invoke-Sql OrderingDb "UPDATE dbo.[Order] SET Status='Accepted' WHERE OrderId='85000000-0000-0000-0000-000000000005';" }
    }
    Invoke-Case 'TC-WF05-11' {
        $null = Invoke-Sql OrderingDb "UPDATE dbo.OrderItem SET UnitPriceSnapshot=99999 WHERE OrderItemId='86000000-0000-0000-0000-000000000001';"
        try {
            $historical = (Get-Report 'dishes' "&itemId=$itemA").items[0]
            Assert-Amount $historical.grossSales.amount $expected.mainItemGross 'Live price replaced paid snapshot/options.'
            $expectedDishName = "B$([char]0x00FA)n b$([char]0x00F2) l$([char]0x1ECB)ch s$([char]0x1EED)"
            Assert-True ($expectedDishName -in $historical.names) 'Historical Vietnamese dish name lost.'
            Assert-True (@($historical.prices).Count -eq 2 -and [decimal]$historical.prices[0].unitPrice -eq 50 -and [decimal]$historical.prices[1].unitPrice -eq 100) 'Historical price/option variants wrong.'
        }
        finally { $null = Invoke-Sql OrderingDb "UPDATE dbo.OrderItem SET UnitPriceSnapshot=80 WHERE OrderItemId='86000000-0000-0000-0000-000000000001';" }
    }
    Invoke-Case 'TC-WF05-12' {
        $main = $dishes.items | Where-Object itemId -eq $itemA
        Assert-Amount $main.cost.knownAmount $expected.mainItemKnownCost 'Historical total line cost multiplied.'
        Assert-Amount $main.grossProfit.knownAmount $expected.mainItemKnownProfit 'Known profit uses different row sets.'
        Assert-True ($null -eq $main.cost.amount -and $null -eq $main.grossProfit.amount) 'Missing cost treated as zero.'
        Assert-True ('DishCostUnavailable' -in $dishes.warnings.code) 'Missing cost warning absent.'
    } 'Partial' 'Historical costs are synthetic; no operational COGS writer.'
    Invoke-Case 'TC-WF05-13' {
        $main = $dishes.items | Where-Object itemId -eq $itemA
        Assert-Amount $sales.totals.discounts.knownAmount $expected.discountsKnown 'Discount/adjustment metadata counted twice.'
        Assert-Amount $sales.totals.dishRevenue.knownAmount $expected.dishRevenueKnown 'VAT/fees included in dish revenue.'
        Assert-Amount $sales.totals.vat.knownAmount $expected.vatKnown 'VAT component wrong.'
        Assert-Amount $sales.totals.serviceFees.knownAmount $expected.serviceFeesKnown 'Service component wrong.'
        Assert-Amount $sales.totals.roundingAdjustment.knownAmount -0.23 'Rounding adjustment component wrong.'
        Assert-Amount $main.discounts.amount 20 'Dish-level discount snapshot wrong.'
        Assert-Amount $main.netSales.amount $expected.mainItemNet 'Dish-level net sales after discount wrong.'
        Assert-True ($null -eq $sales.totals.netDishRevenue.amount) 'Restaurant net revenue fabricated.'
    } 'Partial' 'Discount snapshots covered; operational promotion writer and refunded revenue allocation missing.'
    Invoke-Case 'TC-WF05-14' {
        $gift = $dishes.items | Where-Object itemId -eq $itemGift
        Assert-Amount $gift.netSales.amount 0 'Fully discounted revenue not zero.'
        Assert-Amount $gift.cost.amount 20 'Fully discounted line cost counted more than once.'
        Assert-Amount $gift.grossProfit.amount -20 'Fully discounted contribution wrong.'
        Assert-True ($null -eq $gift.contributionMarginPercent) 'Zero denominator did not return N/A.'
    } 'Partial' 'Fully discounted fixture is not proof of a complimentary-dish writer or restaurant gross-profit implementation.'
    Invoke-Case 'TC-WF05-15' {
        $paid = $dishes.items | Where-Object itemId -eq $itemPaid
        Assert-Amount $paid.grossProfit.amount 20 'Complete line contribution wrong.'
        Assert-Amount $paid.contributionMarginPercent 40 'Complete line margin wrong.'
        Assert-True ('DishContributionBeforeRefunds' -in $dishes.warnings.code) 'Contribution-before-refunds warning absent.'
        $main = $dishes.items | Where-Object itemId -eq $itemA
        Assert-True ($null -eq $main.contributionMarginPercent) 'Incomplete cost produced a margin.'
    } 'Partial' 'Per-dish contribution covered, not whole-restaurant net gross margin.'
    Invoke-Case 'TC-WF05-16' {
        Assert-True ($sales.period.restaurantId -eq $a -and $dishes.period.restaurantId -eq $a) 'Wrong report owner.'
        Assert-Amount $sales.totals.collections.amount $expected.collections 'Authorized manager sees foreign totals.'
        Assert-True ('87000000-0000-0000-0000-000000000099' -notin $dishes.items.itemId) 'Foreign dish leaked into authorized restaurant.'
    }
    Invoke-Case 'TC-WF05-17' {
        $null = Invoke-Api "/api/restaurants/$a/reports/period?$period" '' 401
        $null = Invoke-Api "/api/restaurants/$a/reports/sales?$period" '' 401
        $null = Invoke-Api "/api/restaurants/$a/reports/dishes?$period" '' 401
        foreach ($email in @('cashier@gmail.com', 'waiter@gmail.com', 'admin@gmail.com', 'kitchen@gmail.com', 'inventory@gmail.com', 'platform-admin@example.test')) {
            $otherToken = Login $email
            $null = Invoke-Api "/api/restaurants/$a/reports/sales?$period" $otherToken 403
            $null = Invoke-Api "/api/restaurants/$a/reports/dishes?$period" $otherToken 403
        }
    }
    Invoke-Case 'TC-WF05-18' {
        $null = Invoke-Api "/api/restaurants/$b/reports/sales?$period" $token 403
        $null = Invoke-Api "/api/restaurants/$b/reports/dishes?$period" $token 403
        $null = Invoke-Sql RestaurantDb "INSERT dbo.RestaurantStaff(RestaurantId,UserIdRef,IsActive) VALUES('$b','10000000-0000-0000-0000-000000000002',1);"
        try {
            Assert-Amount (Get-Report 'sales' '' $period $b).totals.collections.amount $expected.otherRestaurantCollections 'Other restaurant fixture not exercised.'
            $otherDishes = Get-Report 'dishes' '' $period $b
            Assert-True (@($otherDishes.items).Count -eq 1 -and [decimal]$otherDishes.items[0].quantity.quantity -eq 10) 'Other restaurant dish isolation wrong.'
            $null = Invoke-Api "/api/restaurants/$a/reports/sales?$period" $token 403 $b
            $null = Invoke-Api "/api/restaurants/$a/reports/dishes?$period" $token 403 $b
        }
        finally {
            $null = Invoke-Sql RestaurantDb "DELETE FROM dbo.RestaurantStaff WHERE RestaurantId='$b' AND UserIdRef='10000000-0000-0000-0000-000000000002';"
        }
        $null = Invoke-Sql RestaurantDb "UPDATE dbo.RestaurantStaff SET IsActive=0 WHERE RestaurantId='$a' AND UserIdRef='10000000-0000-0000-0000-000000000002';"
        try {
            $null = Invoke-Api "/api/restaurants/$a/reports/sales?$period" $token 403
            $null = Invoke-Api "/api/restaurants/$a/reports/dishes?$period" $token 403
        }
        finally { $null = Invoke-Sql RestaurantDb "UPDATE dbo.RestaurantStaff SET IsActive=1 WHERE RestaurantId='$a' AND UserIdRef='10000000-0000-0000-0000-000000000002';" }
    }
    Invoke-Case 'E2E-WF05-01' {
        $current = Get-Report
        $currentDishes = Get-Report 'dishes'
        Assert-Amount $current.totals.collections.amount $expected.collections 'Final refresh changed total.'
        Assert-True (($dishes.items | ConvertTo-Json -Depth 15 -Compress) -ceq ($currentDishes.items | ConvertTo-Json -Depth 15 -Compress)) 'Fault-injection cleanup did not restore source.'
        Assert-True ($before -ceq (Get-FinancialFingerprint)) 'Report changed financial records.'
    } 'Partial' 'Real Docker report flow; seeded provider/cost snapshots synthetic and financial completeness gaps remain.'
    Assert-True (@($cases.Values | Where-Object { $_.execution -ne 'Passed' }).Count -eq 0) 'A required check was not executed.'
}
catch { $failure = $_.Exception.Message }
finally {
    $httpClient.Dispose()
    if (Test-Path -LiteralPath $envFile) {
        if ($project -match '^report-r6-[a-f0-9]{10}$' -and [IO.Path]::GetFullPath($envFile) -eq [IO.Path]::GetFullPath((Join-Path $privateDirectory 'docker.env'))) {
            $down = Invoke-Native 'docker' ($compose + @('down', '--volumes')) (Join-Path $privateDirectory 'cleanup.log')
            $imageCleanupSucceeded = $true
            foreach ($service in @('db-init', 'identity', 'restaurant', 'billing', 'ordering', 'gateway')) {
                $image = "$project-${service}:test"
                $present = Invoke-Native 'docker' @('image', 'inspect', '--format', '{{.Id}}', $image)
                if ($present.ExitCode -eq 0) {
                    $removed = Invoke-Native 'docker' @('image', 'rm', $image)
                    if ($removed.ExitCode -ne 0) {
                        $imageCleanupSucceeded = $false
                    }
                }
            }
            $cleanupSucceeded = $down.ExitCode -eq 0 -and $imageCleanupSucceeded
            Remove-Item -LiteralPath $envFile
        }
    }
    foreach ($key in $originalEnvironment.Keys) { [Environment]::SetEnvironmentVariable($key, $originalEnvironment[$key], 'Process') }
    $resolvedOutput = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName
    $resultsJson = @{
        measuredAt = [DateTimeOffset]::UtcNow.ToString('o')
        project = $project
        sourceCommit = ((Invoke-Native 'git' @('-C', $root, 'rev-parse', 'HEAD')).Output -join '').Trim()
        workingTreeDirty = -not [string]::IsNullOrWhiteSpace(((Invoke-Native 'git' @('-C', $root, 'status', '--porcelain')).Output -join "`n"))
        runnerSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
        fixtureSha256 = @(Get-ChildItem -LiteralPath $fixtures -File | Sort-Object Name | ForEach-Object {
            @{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
        })
        scope = 'Real Docker Gateway/Identity/Billing/Ordering/Restaurant/SQL Server; synthetic fixtures, no provider calls'
        acceptance = 'Incomplete: source gaps and whole-restaurant gross profit are not resolved by fixtures'
        executionSummary = @{
            passed = @($cases.Values | Where-Object execution -eq 'Passed').Count
            failed = @($cases.Values | Where-Object execution -eq 'Failed').Count
            notRun = @($cases.Values | Where-Object execution -eq 'NotRun').Count
        }
        acceptanceSummary = @{
            covered = @($cases.Values | Where-Object acceptance -eq 'Covered').Count
            partial = @($cases.Values | Where-Object acceptance -eq 'Partial').Count
        }
        cases = $cases
        httpChecks = $checks
        failure = $failure
        cleanupSucceeded = $cleanupSucceeded
    } | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText((Join-Path $resolvedOutput 'results.json'), $resultsJson + "`n", $utf8NoBom)
}
if ($failure) { throw $failure }
Assert-True $cleanupSucceeded 'Isolated cleanup failed; inspect the private cleanup log.'
Write-Host "Report checks passed ($($checks.Count) HTTP checks). Acceptance remains incomplete. Results: $(Join-Path $resolvedOutput 'results.json')"
