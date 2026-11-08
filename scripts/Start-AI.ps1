[CmdletBinding()]
param(
    [ValidateSet('Docker', 'Local')]
    [string]$Mode = 'Docker',
    [switch]$SetupOnly,
    [switch]$RuntimeOnly,
    [switch]$Gpu,
    [ValidatePattern('^[a-z0-9][a-z0-9_-]*$')]
    [string]$ProjectName = 'backend'
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$runtime = Join-Path $repository '.ai'
$project = Join-Path $repository 'Services/ai_service/AI.API/AI.API.csproj'
$settingsPath = Join-Path $repository 'Services/ai_service/AI.API/appsettings.json'

function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable failed with exit code $LASTEXITCODE. See the preceding output."
    }
}

function Test-Ollama {
    param([string]$Endpoint)
    try {
        $version = Invoke-RestMethod ($Endpoint.TrimEnd('/') + '/api/version') -TimeoutSec 3
        return -not [string]::IsNullOrWhiteSpace($version.version)
    }
    catch {
        return $false
    }
}

function Find-Ollama {
    $command = Get-Command ollama.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $standard = Join-Path $env:LOCALAPPDATA 'Programs/Ollama/ollama.exe'
    if (Test-Path -LiteralPath $standard) { return $standard }
    $portable = Join-Path $env:LOCALAPPDATA 'RestaurantAI/Ollama'
    if (Test-Path -LiteralPath $portable) {
        $installed = Get-ChildItem -LiteralPath $portable -Filter ollama.exe -Recurse | Select-Object -First 1
        if ($installed) { return $installed.FullName }
    }
    return $null
}

function Install-Ollama {
    if (-not [Environment]::Is64BitOperatingSystem -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64') {
        throw 'Automatic native setup currently supports Windows x64. Install Ollama from https://ollama.com/download/windows, then rerun; or use Docker mode.'
    }
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Write-Host 'No Ollama installation found. Downloading the official standalone Windows x64 release.'
    $release = Invoke-RestMethod 'https://api.github.com/repos/ollama/ollama/releases/latest' -Headers @{ 'User-Agent' = 'RestaurantAI-Setup' } -TimeoutSec 30
    $asset = $release.assets | Where-Object { $_.name -eq 'ollama-windows-amd64.zip' } | Select-Object -First 1
    if (-not $asset -or $asset.digest -notmatch '^sha256:[a-fA-F0-9]{64}$') {
        throw 'The official release has no verifiable Windows archive digest. Install manually from https://ollama.com/download/windows, then rerun. Nothing was installed.'
    }
    $install = Join-Path $env:LOCALAPPDATA ('RestaurantAI/Ollama/' + $release.tag_name)
    if (Test-Path -LiteralPath $install) {
        throw "An incomplete portable installation already exists at $install. Inspect it before installing manually; it will not be overwritten."
    }
    $archive = Join-Path $runtime 'ollama-windows-amd64.zip'
    Invoke-WebRequest $asset.browser_download_url -OutFile $archive -UseBasicParsing -TimeoutSec 1800
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ine $asset.digest.Substring(7)) {
        throw 'Official archive checksum mismatch. Installation was aborted.'
    }
    New-Item -ItemType Directory -Path $install -Force | Out-Null
    Expand-Archive -LiteralPath $archive -DestinationPath $install
    Remove-Item -LiteralPath $archive
    return Join-Path $install 'ollama.exe'
}

try {
    if ($RuntimeOnly -and ($Mode -ne 'Local' -or -not $SetupOnly)) { throw 'RuntimeOnly is reserved for Local SetupOnly development startup.' }
    if ($env:OS -ne 'Windows_NT') { throw 'Run this script on Windows. Docker Compose can also be invoked directly; see README.' }
    $operatingSystem = Get-CimInstance Win32_OperatingSystem
    if ([int]$operatingSystem.BuildNumber -lt 19045) { throw 'Windows 10 22H2 or newer is required. Update Windows before installing this runtime.' }
    New-Item -ItemType Directory -Path $runtime -Force | Out-Null
    $memory = Get-CimInstance Win32_ComputerSystem
    if ($memory.TotalPhysicalMemory -lt 8GB) { throw 'At least 8 GiB host RAM is required for this development setup; 16 GiB or more is recommended. No smaller model will be substituted.' }
    Write-Host ('Host RAM: {0:N1} GiB. GPU is optional; the approved model remains unchanged.' -f ($memory.TotalPhysicalMemory / 1GB))
    $sdk = Get-Command dotnet -ErrorAction SilentlyContinue
    $hasSdk = $false
    if ($sdk) { $hasSdk = @(& dotnet --list-sdks | Where-Object { $_ -match '^9\.' }).Count -gt 0 }
    if ($Mode -eq 'Docker') {
        if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Install Docker Desktop with Linux containers and start it: https://docs.docker.com/desktop/setup/install/windows-install/' }
        & docker info --format '{{.ServerVersion}}' 2>$null
        if ($LASTEXITCODE -ne 0) { throw 'Docker Desktop Linux engine is not ready. Start Docker Desktop, enable WSL 2/Linux containers, then rerun.' }
        $dockerMemory = [long](& docker info --format '{{.MemTotal}}')
        if ($dockerMemory -lt 6GB) { throw 'Allocate at least 8 GiB to Docker Desktop for the backend and approved model, then rerun.' }
        Write-Host "Host .NET 9 SDK detected: $hasSdk. Docker mode uses the repository's .NET 9 SDK images."
        $environmentFile = Join-Path $repository '.env'
        if (-not (Test-Path -LiteralPath $environmentFile)) {
            throw 'Create .env from .env.example and fill in credentials before starting Docker.'
        }
        $compose = @('compose', '--parallel', '2', '--project-directory', $repository, '--project-name', $ProjectName, '--env-file', $environmentFile,
            '-f', (Join-Path $repository 'compose.yaml'), '-f', (Join-Path $repository 'compose.ai.yaml'))
        if ($Gpu) { $compose += @('-f', (Join-Path $repository 'compose.ai.gpu.yaml')) }
        Invoke-Checked docker ($compose + @('config', '--quiet'))
        if (-not $SetupOnly) {
            Write-Host 'Starting the existing backend first; AI is optional and does not gate its services.'
            Invoke-Checked docker ($compose + @('up', '-d', '--build', 'identity', 'restaurant', 'catalog', 'ordering', 'customer', 'inventory', 'billing', 'gateway'))
        }
        Invoke-Checked docker ($compose + @('up', '-d', 'ollama'))
        Invoke-Checked docker ($compose + @('build', 'ai-model-init'))
        Invoke-Checked docker ($compose + @('up', '-d', '--wait', '--wait-timeout', '180', 'ollama'))
        Invoke-Checked docker ($compose + @('run', '--rm', '--no-deps', 'ai-model-init'))
        if (-not $SetupOnly) {
            Invoke-Checked docker ($compose + @('up', '-d', '--no-deps', '--wait', '--wait-timeout', '180', 'ai'))
            Write-Host 'AI is ready. Use the existing Gateway URL and /api/ai routes; see README for QR-session requests.'
        }
        Write-Host "Models persist in the $ProjectName`_ai-models Docker volume. Never use down -v if you want to retain models/data."
        return
    }

    if (-not $RuntimeOnly -and -not $hasSdk) { throw 'Install the .NET 9 SDK from https://dotnet.microsoft.com/download/dotnet/9.0 and rerun. Other installed SDK major versions do not satisfy this check.' }
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    $endpoint = $settings.Ollama.Endpoint
    $local = Join-Path (Split-Path -Parent $settingsPath) 'appsettings.Local.json'
    if (Test-Path -LiteralPath $local) {
        $overrides = Get-Content -LiteralPath $local -Raw | ConvertFrom-Json
        if ($overrides.Ollama.Endpoint) { $endpoint = $overrides.Ollama.Endpoint }
    }
    if ($env:Ollama__Endpoint) { $endpoint = $env:Ollama__Endpoint }
    if (-not (Test-Ollama $endpoint)) {
        $uri = [Uri]$endpoint
        if (-not $uri.IsLoopback -or $uri.Scheme -ne 'http') { throw "Ollama at $endpoint is not reachable. Start the configured runtime; the script will not replace a remote or HTTPS installation." }
        $ollama = Find-Ollama
        if (-not $ollama) {
            $drive = Get-PSDrive -Name ([IO.Path]::GetPathRoot($env:LOCALAPPDATA).Substring(0, 1))
            if ($drive.Free -lt 10GB) { throw 'At least 10 GiB free space is required for a new native runtime/model setup. Choose another location using Ollama documentation or free space.' }
            $ollama = Install-Ollama
        }
        $oldHost = $env:OLLAMA_HOST
        $oldCloud = $env:OLLAMA_NO_CLOUD
        try {
            $env:OLLAMA_HOST = $uri.Authority
            $env:OLLAMA_NO_CLOUD = 'true'
            $process = Start-Process -FilePath $ollama -ArgumentList 'serve' -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $runtime 'ollama.stdout.log') -RedirectStandardError (Join-Path $runtime 'ollama.stderr.log')
            $process.Id | Set-Content (Join-Path $runtime 'ollama.pid')
        }
        finally {
            $env:OLLAMA_HOST = $oldHost
            $env:OLLAMA_NO_CLOUD = $oldCloud
        }
        $deadline = [DateTime]::UtcNow.AddSeconds(60)
        while (-not (Test-Ollama $endpoint)) {
            if ([DateTime]::UtcNow -ge $deadline) { throw "Ollama did not start. Check $runtime\ollama.stderr.log and whether port $($uri.Port) is occupied." }
            Start-Sleep -Seconds 1
        }
    }
    else {
        Write-Host 'Using the existing Ollama runtime without replacing, upgrading or reconfiguring it.'
    }
    $installedModels = Invoke-RestMethod ($endpoint.TrimEnd('/') + '/api/tags') -TimeoutSec 10
    if ([Uri]$endpoint -and ([Uri]$endpoint).IsLoopback -and -not @($installedModels.models | Where-Object { $_.name -eq $settings.Ollama.Model }).Count) {
        $modelDirectory = $env:OLLAMA_MODELS
        if (-not $modelDirectory) { $modelDirectory = [Environment]::GetEnvironmentVariable('OLLAMA_MODELS', 'User') }
        if (-not $modelDirectory) { $modelDirectory = Join-Path $env:USERPROFILE '.ollama/models' }
        $modelRoot = [IO.Path]::GetPathRoot($modelDirectory)
        if ($modelRoot -match '^[a-zA-Z]:\\$') {
            $modelDrive = Get-PSDrive -Name $modelRoot.Substring(0, 1)
            if ($modelDrive.Free -lt 4GB) { throw 'The approved model is missing and its model drive has less than 4 GiB free. Free space or configure OLLAMA_MODELS before starting Ollama.' }
        }
    }
    if ($RuntimeOnly) {
        Write-Host 'Ollama runtime is available. The starting AI process will verify and prepare its model directly.'
        return
    }
    $originalEnvironment = $env:ASPNETCORE_ENVIRONMENT
    $originalBootstrap = $env:AI_DEVELOPMENT_BOOTSTRAP_COMPLETED
    try {
        $env:ASPNETCORE_ENVIRONMENT = 'Development'
        Invoke-Checked dotnet @('run', '--project', $project, '--no-launch-profile', '--', '--prepare-model')
        if (-not $SetupOnly) {
            $env:AI_DEVELOPMENT_BOOTSTRAP_COMPLETED = '1'
            Write-Host 'Starting AI. Run Gateway and the other backend services using the existing local workflow. Ctrl+C stops AI; model files remain on disk.'
            Invoke-Checked dotnet @('run', '--project', $project, '--no-launch-profile')
        }
    }
    finally {
        $env:ASPNETCORE_ENVIRONMENT = $originalEnvironment
        $env:AI_DEVELOPMENT_BOOTSTRAP_COMPLETED = $originalBootstrap
    }
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
