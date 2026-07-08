param(
    [string]$Url = "http://localhost:5167",
    [string]$OutputPath = "artifacts/playwright-screenshot.png",
    [int]$WaitMs = 3000
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutput = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath
} else {
    Join-Path $repoRoot $OutputPath
}

$outputDirectory = Split-Path -Parent $resolvedOutput
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$playwrightVersion = "1.57.0"
$browserRoot = Join-Path $env:LOCALAPPDATA "ms-playwright"
$headlessShell = Join-Path $browserRoot "chromium_headless_shell-1200\chrome-headless-shell-win64\chrome-headless-shell.exe"

$previousOffline = $env:npm_config_offline
$previousRegistry = $env:npm_config_registry

try {
    $env:npm_config_offline = "false"
    $env:npm_config_registry = "https://registry.npmjs.org/"

    if (-not (Test-Path -LiteralPath $headlessShell)) {
        npx.cmd --yes "playwright@$playwrightVersion" install chromium
        if ($LASTEXITCODE -ne 0) {
            throw "Playwright browser installation failed with exit code $LASTEXITCODE."
        }
    }

    npx.cmd --yes "playwright@$playwrightVersion" screenshot "--wait-for-timeout=$WaitMs" $Url $resolvedOutput
    if ($LASTEXITCODE -ne 0) {
        throw "Playwright screenshot failed with exit code $LASTEXITCODE."
    }

    Write-Host "Screenshot captured: $resolvedOutput"
} finally {
    $env:npm_config_offline = $previousOffline
    $env:npm_config_registry = $previousRegistry
}
