param(
    [switch]$Force,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$baseUrl = "https://huggingface.co/yuniko-software/bge-m3-onnx/resolve/main"
$files = @(
    "bge_m3_model.onnx",
    "bge_m3_model.onnx_data",
    "bge_m3_tokenizer.onnx"
)

$targetDirectory = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "bge_m3"

if (-not $DryRun -and -not (Test-Path -LiteralPath $targetDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $targetDirectory | Out-Null
}

foreach ($file in $files) {
    $url = "$baseUrl/$file"
    $destination = Join-Path $targetDirectory $file

    if ($DryRun) {
        Write-Host "Would download $url -> $destination"
        continue
    }

    if ((Test-Path -LiteralPath $destination -PathType Leaf) -and -not $Force) {
        Write-Host "Skipping existing file: $destination"
        continue
    }

    $temporaryFile = "$destination.download"
    if (Test-Path -LiteralPath $temporaryFile -PathType Leaf) {
        Remove-Item -LiteralPath $temporaryFile -Force
    }

    Write-Host "Downloading $file..."
    Invoke-WebRequest -Uri $url -OutFile $temporaryFile -UseBasicParsing

    if ((Get-Item -LiteralPath $temporaryFile).Length -le 0) {
        Remove-Item -LiteralPath $temporaryFile -Force
        throw "Downloaded file is empty: $file"
    }

    Move-Item -LiteralPath $temporaryFile -Destination $destination -Force
    Write-Host "Saved $destination"
}

if ($DryRun) {
    Write-Host "Dry run complete. No files were downloaded."
} else {
    Write-Host "BGE-M3 ONNX files are ready in $targetDirectory"
}
