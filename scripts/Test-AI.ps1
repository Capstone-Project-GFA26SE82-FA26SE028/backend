[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [Uri]$Gateway,
    [Parameter(Mandatory = $true)]
    [Guid]$SessionId,
    [Parameter(Mandatory = $true)]
    [Guid]$CartId,
    [Parameter(Mandatory = $true)]
    [string]$CookieFile,
    [string]$OutputFile
)

$ErrorActionPreference = 'Stop'
$web = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$cookies = Get-Content -LiteralPath $CookieFile -Raw | ConvertFrom-Json
$name = 'guest_access_' + $SessionId.ToString('N')
if (-not $cookies.$name) { throw "Cookie file must contain $name from the existing guest-access join API. Do not commit it. This test does not create a QR session or cart." }
$base = $Gateway.AbsoluteUri.TrimEnd('/')
$web.Cookies.Add([Uri]($base + '/api'), (New-Object System.Net.Cookie($name, $cookies.$name, '/api')))
function Post-AI {
    param([string]$Path, [object]$Body)
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $response = Invoke-RestMethod -Uri ($base + $Path) -Method Post -WebSession $web -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 8) -TimeoutSec 95
    if (-not $response.isSuccess) { throw 'AI request failed.' }
    return @{ milliseconds = $watch.Elapsed.TotalMilliseconds; response = $response }
}
$root = "/api/ai/table-sessions/$SessionId"
$before = Invoke-RestMethod "$base/api/table-sessions/$SessionId/carts/$CartId" -WebSession $web
$chat = Post-AI "$root/chat" @{ message = 'What drinks can I order?'; history = @() }
if (@($chat.response.result.items).Count -eq 0) { throw 'UC2 did not return any menu cards for the drinks request. Check the real menu and model classification; a generic fallback is not a successful menu test.' }
$recommendations = Post-AI "$root/carts/$CartId/recommendations" @{ limit = 3 }
$allergy = Post-AI "$root/chat" @{ message = 'I have a peanut allergy. What is safe?'; history = @() }
$refusal = Post-AI "$root/chat" @{ message = 'Write a Python web scraper'; history = @() }
$after = Invoke-RestMethod "$base/api/table-sessions/$SessionId/carts/$CartId" -WebSession $web
if ($before.result.version -ne $after.result.version) { throw 'The cart version changed during this read-only AI smoke test.' }
if (@($recommendations.response.result.items).Count -eq 0) { throw 'No recommendations were returned. Ensure the real menu has an orderable item outside the cart.' }
foreach ($item in $recommendations.response.result.items) {
    if (@($before.result.items | Where-Object { $_.itemId -eq $item.item.itemId }).Count -gt 0 -or -not $item.item.canOrder) { throw 'Recommendation violated cart exclusion or CanOrder.' }
}
if ($allergy.response.result.items.Count -ne 0 -or $allergy.response.result.answer -notmatch 'staff') { throw 'Allergy fallback did not direct the customer to staff.' }
if ($refusal.response.result.items.Count -ne 0 -or $refusal.response.result.answer -notmatch 'public menu') { throw 'Out-of-scope refusal failed.' }
$report = [ordered]@{
    measuredAt = [DateTimeOffset]::UtcNow.ToString('O')
    gateway = $base
    sessionId = $SessionId
    cartId = $CartId
    chat = $chat
    recommendations = $recommendations
    allergy = $allergy
    refusal = $refusal
    cartUnchanged = $true
    scope = 'Real Gateway, AI, Catalog, Restaurant, Ordering and Gemini API. No AI data-write endpoint was invoked.'
}
$json = $report | ConvertTo-Json -Depth 15
if ($OutputFile) { [IO.File]::WriteAllText($OutputFile, $json) }
$json
