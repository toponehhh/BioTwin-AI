param(
    [string]$OllamaBaseUrl = "http://localhost:11434",
    [string]$All2MdPath = "",
    [int]$BioTwinPort = 5166,
    [int]$All2MdPort = 8000,
    [switch]$SkipOllamaCheck,
    [switch]$SkipAll2Md,
    [switch]$Restart
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectPath = Join-Path $Root "src\BioTwin_AI\BioTwin_AI.csproj"
$LogDir = Join-Path $Root ".local-logs"
$DefaultAll2MdPath = Join-Path (Split-Path -Parent $Root) "All2MD"

if ([string]::IsNullOrWhiteSpace($All2MdPath)) {
    $All2MdPath = $DefaultAll2MdPath
}

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Test-Url {
    param(
        [string]$Url,
        [int]$TimeoutSeconds = 5
    )

    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec $TimeoutSeconds
        return $response.StatusCode -ge 200 -and $response.StatusCode -lt 500
    }
    catch {
        return $false
    }
}

function Wait-Url {
    param(
        [string]$Url,
        [int]$TimeoutSeconds = 60
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Url -Url $Url -TimeoutSeconds 5) {
            return $true
        }

        Start-Sleep -Seconds 2
    }

    return $false
}

function Get-CommandPath {
    param([string]$Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    return $null
}

function Get-DotnetPath {
    $candidates = @()

    $pathDotnet = Get-CommandPath "dotnet"
    if ($pathDotnet) {
        $candidates += $pathDotnet
    }

    $localDotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
    $programFilesDotnet = "C:\Program Files\dotnet\dotnet.exe"
    $candidates += $localDotnet
    $candidates += $programFilesDotnet

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (-not (Test-Path $candidate)) {
            continue
        }

        $sdks = & $candidate --list-sdks 2>$null
        if ($LASTEXITCODE -ne 0) {
            continue
        }

        foreach ($sdk in $sdks) {
            if ($sdk -match "^(?<major>\d+)\.") {
                if ([int]$Matches.major -ge 10) {
                    return $candidate
                }
            }
        }
    }

    throw "Could not find a .NET 10 SDK. Install .NET 10 or add it to PATH."
}

function Start-LocalProcess {
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$WorkingDirectory,
        [string]$StdoutPath,
        [string]$StderrPath
    )

    return Start-Process `
        -FilePath $FilePath `
        -ArgumentList $ArgumentList `
        -WorkingDirectory $WorkingDirectory `
        -RedirectStandardOutput $StdoutPath `
        -RedirectStandardError $StderrPath `
        -WindowStyle Hidden `
        -PassThru
}

function Stop-PortProcess {
    param([int]$Port)

    $lines = netstat -ano | Select-String -Pattern ":$Port\s+.*LISTENING"
    foreach ($line in $lines) {
        $parts = ($line.ToString() -split "\s+") | Where-Object { $_ }
        $pidText = $parts[-1]
        if ($pidText -match "^\d+$") {
            $pidValue = [int]$pidText
            $process = Get-Process -Id $pidValue -ErrorAction SilentlyContinue
            if ($process) {
                Write-Host "Stopping process $($process.ProcessName) ($pidValue) on port $Port"
                Stop-Process -Id $pidValue -Force
            }
        }
    }
}

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

if (-not (Test-Path $ProjectPath)) {
    throw "BioTwin project file not found: $ProjectPath"
}

if ($Restart) {
    Write-Step "Stopping existing local services"
    Stop-PortProcess -Port $BioTwinPort
    if (-not $SkipAll2Md) {
        Stop-PortProcess -Port $All2MdPort
    }
    Start-Sleep -Seconds 2
}

if (-not $SkipOllamaCheck) {
    Write-Step "Checking Ollama at $OllamaBaseUrl"
    $ollamaTagsUrl = "$($OllamaBaseUrl.TrimEnd('/'))/api/tags"
    if (-not (Test-Url -Url $ollamaTagsUrl -TimeoutSeconds 10)) {
        throw "Ollama is not reachable at $ollamaTagsUrl. Start Ollama first, or pass -OllamaBaseUrl <url>. If Ollama runs in WSL, make sure it is exposed to Windows, for example OLLAMA_HOST=0.0.0.0:11434 ollama serve."
    }
    Write-Host "Ollama is reachable."
}

if (-not $SkipAll2Md) {
    Write-Step "Starting or reusing All2MD on port $All2MdPort"
    $all2MdHealthUrl = "http://localhost:$All2MdPort/health"

    if (Test-Url -Url $all2MdHealthUrl -TimeoutSeconds 3) {
        Write-Host "All2MD is already running at $all2MdHealthUrl"
    }
    else {
        if (-not (Test-Path $All2MdPath)) {
            throw "All2MD directory not found: $All2MdPath. Pass -All2MdPath <path>, or use -SkipAll2Md."
        }

        $uvPath = Get-CommandPath "uv"
        if (-not $uvPath) {
            $uvPath = Join-Path $env:USERPROFILE ".local\bin\uv.exe"
        }

        if (-not (Test-Path $uvPath)) {
            throw "uv was not found. Install uv or add it to PATH."
        }

        $env:UV_CACHE_DIR = Join-Path $All2MdPath ".uv-cache"
        $env:PYTHONIOENCODING = "utf-8"
        $env:PYTHONUTF8 = "1"

        $all2MdOut = Join-Path $LogDir "all2md.out.log"
        $all2MdErr = Join-Path $LogDir "all2md.err.log"
        Remove-Item -LiteralPath $all2MdOut,$all2MdErr -ErrorAction SilentlyContinue

        $all2MdProcess = Start-LocalProcess `
            -FilePath $uvPath `
            -ArgumentList @("run", "python", "run_server.py") `
            -WorkingDirectory $All2MdPath `
            -StdoutPath $all2MdOut `
            -StderrPath $all2MdErr

        if (-not (Wait-Url -Url $all2MdHealthUrl -TimeoutSeconds 120)) {
            Write-Host "All2MD stdout log: $all2MdOut"
            Write-Host "All2MD stderr log: $all2MdErr"
            throw "All2MD did not become healthy at $all2MdHealthUrl. Process id: $($all2MdProcess.Id)"
        }

        Write-Host "All2MD started. Process id: $($all2MdProcess.Id)"
    }
}

Write-Step "Starting or reusing BioTwin on port $BioTwinPort"
$bioTwinUrl = "http://localhost:$BioTwinPort"

if (Test-Url -Url $bioTwinUrl -TimeoutSeconds 3) {
    Write-Host "BioTwin is already running at $bioTwinUrl"
}
else {
    $dotnetPath = Get-DotnetPath
    $bioTwinOut = Join-Path $LogDir "biotwin.out.log"
    $bioTwinErr = Join-Path $LogDir "biotwin.err.log"
    Remove-Item -LiteralPath $bioTwinOut,$bioTwinErr -ErrorAction SilentlyContinue

    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:ASPNETCORE_URLS = $bioTwinUrl
    $env:All2MD__ApiUrl = "http://localhost:$All2MdPort"
    $env:LLM__Provider = "Ollama"
    $env:LLM__BaseUrl = $OllamaBaseUrl.TrimEnd("/")

    $bioTwinProcess = Start-LocalProcess `
        -FilePath $dotnetPath `
        -ArgumentList @("run", "--project", $ProjectPath, "--no-launch-profile") `
        -WorkingDirectory $Root `
        -StdoutPath $bioTwinOut `
        -StderrPath $bioTwinErr

    if (-not (Wait-Url -Url $bioTwinUrl -TimeoutSeconds 120)) {
        Write-Host "BioTwin stdout log: $bioTwinOut"
        Write-Host "BioTwin stderr log: $bioTwinErr"
        throw "BioTwin did not become available at $bioTwinUrl. Process id: $($bioTwinProcess.Id)"
    }

    Write-Host "BioTwin started. Process id: $($bioTwinProcess.Id)"
}

Write-Step "Local services are ready"
Write-Host "BioTwin: $bioTwinUrl"
if (-not $SkipAll2Md) {
    Write-Host "All2MD: http://localhost:$All2MdPort"
}
Write-Host "Ollama: $OllamaBaseUrl"
Write-Host "Logs: $LogDir"
