#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [Uri]$Gateway,
    [Parameter(Mandatory)] [Guid]$SessionId,
    [Parameter(Mandatory)] [Guid]$CartId,
    [Parameter(Mandatory)] [string]$CookieFile,
    [Parameter(Mandatory)] [string]$AiContainer,
    [string]$Dataset = (Join-Path $PSScriptRoot 'ai/acceptance-cases.json'),
    [string]$OutputFile = (Join-Path $PSScriptRoot '../.ai/a5/acceptance.json'),
    [int]$TargetLatencyMs = 3000,
    [ValidateRange(1, 10)] [int]$Repetitions = 1,
    [Guid]$OtherGuestCartId = [Guid]::Empty
)

$ErrorActionPreference = 'Stop'
$base = $Gateway.AbsoluteUri.TrimEnd('/')
$web = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
$cookies = Get-Content -LiteralPath $CookieFile -Raw | ConvertFrom-Json
$cookieName = 'guest_access_' + $SessionId.ToString('N')
if (-not $cookies.$cookieName) { throw "Cookie file is missing $cookieName. Obtain it through QR join. Do not commit credentials." }
$web.Cookies.Add([Uri]($base + '/api'), [Net.Cookie]::new($cookieName, $cookies.$cookieName, '/api'))
$cases = @(Get-Content -LiteralPath $Dataset -Raw | ConvertFrom-Json)
if ($cases.Count -eq 0) { throw 'Acceptance dataset must contain at least one case.' }
foreach ($case in $cases) {
    foreach ($id in @($case.requiredItems) + @($case.forbiddenItems) + @($case.allowedItems)) {
        if ($id -and -not [Guid]::TryParse($id, [ref][Guid]::Empty)) { throw "Invalid item ID in case $($case.id)" }
    }
}
if ($OtherGuestCartId -ne [Guid]::Empty) {
    $cases += [pscustomobject]@{ id = 'N04'; function = 'recommendation'; cartId = $OtherGuestCartId.ToString(); status = 404; model = $false }
}
$results = [Collections.Generic.List[object]]::new()
$contexts = @{}
$start = [DateTimeOffset]::UtcNow
$runWatch = [Diagnostics.Stopwatch]::StartNew()
$before = Invoke-RestMethod "$base/api/table-sessions/$SessionId/carts/$CartId" -WebSession $web -TimeoutSec 95
if (@($before.result.items).Count -eq 0) { throw 'Populate the test cart first. An empty cart cannot prove model inference.' }
$cartItemIds = @($before.result.items | ForEach-Object itemId)

for ($run = 1; $run -le $Repetitions; $run++) {
    $contexts.Clear()
    foreach ($case in $cases) {
        $trace = [Guid]::NewGuid().ToString('N')
        $span = [Guid]::NewGuid().ToString('N').Substring(0, 16)
        $path = "/api/ai/table-sessions/$SessionId/chat"
        $body = @{ message = $case.message }
        if ($case.function -eq 'recommendation') {
            $targetCart = if ($case.cartId) { $case.cartId } else { $CartId }
            $path = "/api/ai/table-sessions/$SessionId/carts/$targetCart/recommendations"
            $body = @{ limit = 3 }
        }
        if ($case.conversation -and $contexts.ContainsKey($case.conversation)) {
            $body.conversationContext = $contexts[$case.conversation]
        }
        if ($case.invalidContext) { $body.conversationContext = 'invalid-acceptance-context' }
        $arguments = @{
            Uri = $base + $path
            Method = 'Post'
            ContentType = 'application/json; charset=utf-8'
            Body = [Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 8 -Compress))
            Headers = @{ traceparent = "00-$trace-$span-01" }
            TimeoutSec = 95
            SkipHttpErrorCheck = $true
        }
        if (-not $case.anonymous) { $arguments.WebSession = $web }
        $failures = [Collections.Generic.List[string]]::new()
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $status = 0
        $reply = $null
        try {
            $http = Invoke-WebRequest @arguments
            $watch.Stop()
            $status = [int]$http.StatusCode
            $reply = $http.Content | ConvertFrom-Json
        }
        catch {
            $watch.Stop()
            $failures.Add('Request failed or response was not JSON. No provider body or credentials were saved.')
        }
        $expectedStatus = if ($case.status) { [int]$case.status } else { 200 }
        if ($status -ne $expectedStatus) { $failures.Add("Expected HTTP $expectedStatus, got $status") }
        $cards = @()
        $answer = $null
        if ($status -eq 200 -and $reply) {
            $payload = $reply.result
            if (-not $reply.isSuccess) { $failures.Add('Success envelope missing') }
            $answer = if ($case.function -eq 'chat') { $payload.answer } else { $payload.message }
            $cards = @(if ($case.function -eq 'chat') { $payload.items } else { $payload.items | ForEach-Object item })
            if ($case.conversation) { $contexts[$case.conversation] = $payload.conversationContext }
            if ($null -ne $case.minimumItems -and $cards.Count -lt $case.minimumItems) { $failures.Add('Too few menu cards') }
            if ($null -ne $case.maximumItems -and $cards.Count -gt $case.maximumItems) { $failures.Add('Too many menu cards') }
            $ids = @($cards | ForEach-Object itemId)
            foreach ($id in $case.requiredItems) { if ($id -notin $ids) { $failures.Add("Missing required item $id") } }
            foreach ($id in $case.forbiddenItems) { if ($id -in $ids) { $failures.Add("Forbidden item $id") } }
            if ($case.allowedItems) { foreach ($id in $ids) { if ($id -notin $case.allowedItems) { $failures.Add("Unexpected item $id") } } }
            foreach ($pattern in $case.answerPatterns) { if ($answer -notmatch $pattern) { $failures.Add("Answer did not match $pattern") } }
            foreach ($card in $cards) {
                try {
                    $current = (Invoke-RestMethod "$base/api/menu/$($card.itemId)?tableSessionId=$SessionId&channel=DineIn" -WebSession $web -TimeoutSec 95).result
                    foreach ($field in @('itemId', 'itemName', 'price', 'priceId', 'availability', 'canOrder')) {
                        if ([string]$card.$field -cne [string]$current.$field) { $failures.Add("Catalog mismatch $($card.itemId) $field") }
                    }
                    $requiresOptions = @($current.optionGroups | Where-Object { $_.minSelection -gt 0 }).Count -gt 0
                    if ([bool]$card.requiresOptionSelection -ne $requiresOptions) { $failures.Add("Catalog option selection mismatch $($card.itemId)") }
                }
                catch { $failures.Add("Catalog verification failed for $($card.itemId)") }
                if ($case.function -eq 'recommendation' -and (-not $card.canOrder -or $card.itemId -in $cartItemIds)) {
                    $failures.Add('Recommendation violated CanOrder or ItemId cart exclusion')
                }
            }
            if ($case.function -eq 'recommendation') {
                foreach ($suggestion in $payload.items) {
                    if ($suggestion.reason -notin @('Món ăn kèm để bạn cân nhắc cùng các món trong giỏ.', 'Một lựa chọn thêm cho bữa ăn của bạn.', 'Gợi ý dựa trên các món hiện có trong giỏ của bạn.')) {
                        $failures.Add('Recommendation reason is not controlled Vietnamese')
                    }
                }
            }
        }
        $results.Add([pscustomobject]@{
            id = $case.id; run = $run; function = $case.function; question = $case.message
            endpoint = $path; traceId = $trace; status = $status
            milliseconds = [Math]::Round($watch.Elapsed.TotalMilliseconds, 2)
            answer = $answer; items = $cards; expectation = $case; expectedModel = $case.model
            modelObserved = $null; modelAttempts = 0; retryCount = 0; usage = $null; timings = $null; failures = $failures
        })
        Write-Host "$($case.id) run $run HTTP $status $([Math]::Round($watch.Elapsed.TotalMilliseconds)) ms"
    }
}
$runWatch.Stop()
$after = Invoke-RestMethod "$base/api/table-sessions/$SessionId/carts/$CartId" -WebSession $web -TimeoutSec 95
$cartUnchanged = $before.result.version -eq $after.result.version
$logLines = @(& docker logs --since $start.ToString('O') $AiContainer 2>&1 | ForEach-Object ToString)
if ($LASTEXITCODE -ne 0) { throw 'Cannot read AI logs. Acceptance results require evidence from the actual AI container.' }
$usagePattern = 'GeminiUsage TraceId=(?<trace>[a-f0-9]{32}) Operation=(?<operation>\w+) PromptTokens=(?<prompt>\d+|unknown) OutputTokens=(?<output>\d+|unknown) ThoughtTokens=(?<thought>\d+|unknown) TotalTokens=(?<total>\d+|unknown)'
$usages = @{}
$timings = @{}
$retries = @{}
foreach ($line in $logLines) {
    if ($line -match 'GeminiRetry TraceId=(?<trace>[a-f0-9]{32}) Attempt=2') {
        if (-not $retries.ContainsKey($Matches.trace)) { $retries[$Matches.trace] = 0 }
        $retries[$Matches.trace]++
    }
    if ($line -match 'AITiming TraceId=(?<trace>[a-f0-9]{32}) Stage=(?<stage>GuestAccess|MenuRead|MenuRefresh|CartRead|Gemini|Total) Milliseconds=(?<ms>\d+(?:\.\d+)?)') {
        $traceId = $Matches.trace
        $entry = [pscustomobject]@{
            stage = $Matches.stage
            milliseconds = [double]::Parse($Matches.ms, [Globalization.CultureInfo]::InvariantCulture)
        }
        if (-not $timings.ContainsKey($traceId)) { $timings[$traceId] = @() }
        $timings[$traceId] += $entry
    }
    if ($line -match $usagePattern) {
        $entry = @{}
        foreach ($field in @('prompt', 'output', 'thought', 'total')) {
            $entry[$field] = if ($Matches[$field] -eq 'unknown') { $null } else { [long]$Matches[$field] }
        }
        if (-not $usages.ContainsKey($Matches.trace)) { $usages[$Matches.trace] = @() }
        $usages[$Matches.trace] += [pscustomobject]$entry
    }
}
foreach ($result in $results) {
    $matched = @($usages[$result.traceId] | Where-Object { $null -ne $_ })
    $result.modelObserved = $matched.Count -gt 0
    $result.usage = $matched
    $result.timings = @($timings[$result.traceId] | Where-Object { $null -ne $_ })
    $result.modelAttempts = @($result.timings | Where-Object stage -eq Gemini).Count
    $result.retryCount = if ($retries.ContainsKey($result.traceId)) { $retries[$result.traceId] } else { 0 }
    if ($result.expectedModel -and -not $result.modelObserved) { $result.failures.Add('No correlated Gemini usage observed. Inference is unverified.') }
    if ($result.modelObserved -and ('Gemini' -notin $result.timings.stage -or 'Total' -notin $result.timings.stage)) {
        $result.failures.Add('Missing correlated Gemini/Total timing. Rebuild AI and enable Information logs before measuring.')
    }
    if ($matched.Count -gt 1) { $result.failures.Add('More than one correlated model response for one API request') }
    if (-not $result.expectedModel -and ($result.modelObserved -or $result.modelAttempts -gt 0)) { $result.failures.Add('Unexpected model HTTP attempt') }
    if ($result.modelAttempts -gt 2 -or $result.retryCount -gt 1) { $result.failures.Add('Model exceeded the approved retry limit') }
    if (-not $cartUnchanged) { $result.failures.Add('Cart changed during read-only benchmark') }
}
function Measure-Group([object[]]$Rows) {
    $times = @($Rows.milliseconds | Sort-Object)
    if ($times.Count -eq 0) { return $null }
    return [ordered]@{
        samples = $times.Count
        meanMs = [Math]::Round(($times | Measure-Object -Average).Average, 2)
        p50Ms = $times[[Math]::Ceiling(0.5 * $times.Count) - 1]
        p95Ms = $times[[Math]::Ceiling(0.95 * $times.Count) - 1]
        minMs = $times[0]; maxMs = $times[-1]
        withinTarget = @($times | Where-Object { $_ -le $TargetLatencyMs }).Count
    }
}
$passed = @($results | Where-Object { $_.failures.Count -eq 0 }).Count
$tokens = @($results | ForEach-Object usage | Where-Object { $null -ne $_.total })
$firstModel = $results | Where-Object modelObserved | Select-Object -First 1
$phaseRows = foreach ($row in $results | Where-Object { $_.modelObserved -and $_.status -eq 200 }) {
    foreach ($phase in $row.timings | Group-Object stage) {
        [pscustomobject]@{ stage = $phase.Name; milliseconds = ($phase.Group.milliseconds | Measure-Object -Sum).Sum }
    }
}
$phaseSummary = foreach ($phase in $phaseRows | Group-Object stage) {
    [ordered]@{ stage = $phase.Name; stats = Measure-Group @($phase.Group) }
}
$inspection = @(& docker inspect $AiContainer | ConvertFrom-Json)[0]
$modelOverride = $inspection.Config.Env | Where-Object { $_ -like 'Gemini__Model=*' } | Select-Object -First 1
$containerSettings = (& docker exec $AiContainer cat /app/appsettings.json | Out-String) | ConvertFrom-Json
$modelName = if ($modelOverride) { $modelOverride.Substring('Gemini__Model='.Length) } else { $containerSettings.Gemini.Model }
$report = [ordered]@{
    measuredAt = $start.ToString('O'); head = (& git -C $PSScriptRoot rev-parse HEAD)
    datasetSha256 = (Get-FileHash -LiteralPath $Dataset -Algorithm SHA256).Hash
    scope = 'Synthetic acceptance requests via Docker Gateway and real Gemini. Catalog verified immediately after each response. No frontend or human trial.'
    scoring = 'Explicit assertions only. This pass rate is not general model accuracy, precision, recall or F1.'
    percentiles = 'Nearest rank. Latency covers HTTP request and complete response only, excludes subsequent Catalog assertions.'
    cloudWarmup = 'Cloud model loading state is unobservable. First request is not a measured cold model start.'
    model = $modelName; aiImageId = $inspection.Image
    aiContainerStartedAt = $inspection.State.StartedAt
    powershellVersion = $PSVersionTable.PSVersion.ToString()
    targetLatencyMs = $TargetLatencyMs; cases = $results.Count; passed = $passed
    passRatePercent = [Math]::Round(100 * $passed / $results.Count, 2)
    cartUnchanged = $cartUnchanged
    successfulChat = Measure-Group @($results | Where-Object { $_.function -eq 'chat' -and $_.status -eq 200 })
    successfulRecommendations = Measure-Group @($results | Where-Object { $_.function -eq 'recommendation' -and $_.status -eq 200 })
    chatWithoutInference = Measure-Group @($results | Where-Object { $_.function -eq 'chat' -and $_.status -eq 200 -and -not $_.modelObserved })
    modelObservedLatency = Measure-Group @($results | Where-Object { $_.modelObserved -and $_.status -eq 200 })
    stageTimingsForModelRequests = @($phaseSummary)
    firstModelRequestMs = $firstModel.milliseconds
    subsequentModelLatency = Measure-Group @($results | Where-Object { $_.modelObserved -and $_ -ne $firstModel -and $_.status -eq 200 })
    tokenMeasuredCalls = $tokens.Count
    knownTotalTokens = if ($tokens.Count) { ($tokens.total | Measure-Object -Sum).Sum } else { $null }
    elapsedSecondsIncludingAssertions = [Math]::Round($runWatch.Elapsed.TotalSeconds, 2)
    results = $results
}
$destination = [IO.Path]::GetFullPath($OutputFile)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
[IO.File]::WriteAllText($destination, ($report | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
Write-Host "Acceptance $passed/$($results.Count). Report $destination"
if ($passed -ne $results.Count) { exit 1 }
