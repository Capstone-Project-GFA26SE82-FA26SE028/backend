[CmdletBinding()]
param(
    [ValidatePattern('^[a-z0-9][a-z0-9_-]*$')]
    [string]$ProjectName
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repository
try {
    docker info --format '{{.ServerVersion}}'
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker Engine is unavailable. Start Docker Desktop and run the script again.'
    }

    $compose = @('compose')
    if ($ProjectName) {
        $compose += @('--project-name', $ProjectName)
    }

    docker @compose up -d --build
    if ($LASTEXITCODE -ne 0) {
        throw 'Compose failed to start the backend. Check the preceding output.'
    }
}
finally {
    Pop-Location
}
