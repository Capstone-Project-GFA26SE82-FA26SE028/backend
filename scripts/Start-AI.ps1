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
        throw "$Executable failed with exit code $LASTEXITCODE. Review the preceding readiness/configuration message."
    }
}

try {
    if ($Mode -eq 'Docker') {
        if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
            throw 'Install and start Docker Desktop with Linux containers before running this command.'
        }
        Invoke-Checked docker @('info', '--format', '{{.ServerVersion}}')
        $compose = @('compose', '--project-directory', $repository, '--project-name', $ProjectName,
            '-f', (Join-Path $repository 'compose.yaml'), '-f', (Join-Path $repository 'compose.ai.yaml'))
        Invoke-Checked docker ($compose + @('config', '--quiet'))
        if ($SetupOnly) {
            Invoke-Checked docker ($compose + @('build', 'ai'))
            Invoke-Checked docker ($compose + @('run', '--rm', '--no-deps', 'ai', '--check-provider'))
        }
        else {
            Invoke-Checked docker ($compose + @('up', '-d', '--build'))
            Write-Host 'Backend started. AI uses Gemini only; check AI readiness and its API key if AI is unavailable.'
        }
        return
    }

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue) -or -not ((& dotnet --list-sdks) -match '^9\.')) {
        throw 'Install the .NET 9 SDK before starting the native AI service.'
    }
    $oldKey = $env:Gemini__ApiKey
    $oldModel = $env:Gemini__Model
    try {
        $environmentFile = Join-Path $repository '.env'
        if (Test-Path -LiteralPath $environmentFile) {
            foreach ($line in Get-Content -LiteralPath $environmentFile) {
                if ($line -match '^\s*GEMINI_API_KEY\s*=\s*(.*?)\s*$' -and -not $env:Gemini__ApiKey -and -not $env:GEMINI_API_KEY) {
                    $env:Gemini__ApiKey = $Matches[1].Trim('"').Trim("'")
                }
                elseif ($line -match '^\s*GEMINI_MODEL\s*=\s*(.*?)\s*$' -and -not $env:Gemini__Model) {
                    $env:Gemini__Model = $Matches[1].Trim('"').Trim("'")
                }
            }
        }
        if ($SetupOnly) {
            Invoke-Checked dotnet @('run', '--project', $project, '--no-launch-profile', '--', '--check-provider')
        }
        else {
            Write-Host 'Starting the Gemini AI service. Keep Gateway, Restaurant, Catalog and Ordering running using the usual backend workflow.'
            Invoke-Checked dotnet @('run', '--project', $project, '--no-launch-profile')
        }
    }
    finally {
        $env:Gemini__ApiKey = $oldKey
        $env:Gemini__Model = $oldModel
    }
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
