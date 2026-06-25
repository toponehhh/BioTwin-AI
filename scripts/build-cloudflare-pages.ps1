param(
    [string]$DotNetPath = "C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe",
    [string]$Configuration = "Release",
    [string]$OutputPath = "artifacts\cloudflare-pages"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$absoluteOutput = Join-Path $root $OutputPath

Set-Location $root
& $DotNetPath publish .\src\BioTwin_AI.BlazorClient\BioTwin_AI.BlazorClient.csproj -c $Configuration -o $absoluteOutput -p:UseAppHost=false

Write-Host "Cloudflare Pages artifact: $absoluteOutput\wwwroot"
