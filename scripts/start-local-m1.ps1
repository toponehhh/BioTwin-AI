param(
    [string]$DotNetPath = "C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe",
    [int]$ApiPort = 5014,
    [int]$ClientPort = 5193
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$logs = Join-Path $root "artifacts\devserver"
New-Item -ItemType Directory -Force -Path $logs | Out-Null

$apiLog = Join-Path $logs "api.combined.log"
$clientLog = Join-Path $logs "client.combined.log"

$apiCommand = "`$env:ASPNETCORE_ENVIRONMENT='Development'; Set-Location '$root'; & '$DotNetPath' run --project .\src\BioTwin_AI.AspNetCoreApi\BioTwin_AI.AspNetCoreApi.csproj --urls http://localhost:$ApiPort *> '$apiLog'"
$clientCommand = "Set-Location '$root'; & '$DotNetPath' run --project .\src\BioTwin_AI.BlazorClient\BioTwin_AI.BlazorClient.csproj --urls http://localhost:$ClientPort *> '$clientLog'"

$api = Start-Process powershell.exe -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", $apiCommand) -WindowStyle Hidden -PassThru
$client = Start-Process powershell.exe -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", $clientCommand) -WindowStyle Hidden -PassThru

Write-Host "API:    http://localhost:$ApiPort (host process $($api.Id), log $apiLog)"
Write-Host "Client: http://localhost:$ClientPort (host process $($client.Id), log $clientLog)"
