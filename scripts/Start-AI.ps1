[CmdletBinding()]
param(
    [ValidateSet('Docker', 'Local')]
    [string]$Mode = 'Docker',
    [switch]$SetupOnly,
    [ValidatePattern('^[a-z0-9][a-z0-9_-]*$')]
    [string]$ProjectName = 'backend'
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository 'Services/ai_service/AI.API/AI.API.csproj'

function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable failed with exit code $LASTEXITCODE."
    }
}

try {
    Push-Location $repository
    if ($Mode -eq 'Docker') {
        if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
            throw 'Install Docker Desktop and start its Linux engine.'
        }
        Invoke-Checked docker @('info', '--format', '{{.ServerVersion}}')
        Invoke-Checked docker @('compose', '-p', $ProjectName, 'config', '--quiet')
        if (-not $SetupOnly) {
            Invoke-Checked docker @('compose', '-p', $ProjectName, 'up', '-d', '--build')
            Write-Host 'Started the backend. Set GEMINI_API_KEY in the ignored root .env. Check docker compose logs ai for dependency errors.'
        }
    }
    else {
        if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
            throw 'Install the .NET 9 SDK.'
        }
        if (-not @(& dotnet --list-sdks | Where-Object { $_ -match '^9\.' }).Count) {
            throw 'Install the .NET 9 SDK.'
        }
        if (-not $SetupOnly) {
            Invoke-Checked dotnet @('run', '--project', $project)
        }
    }
}
catch {
    Write-Error $_
    exit 1
}
finally {
    Pop-Location
}
