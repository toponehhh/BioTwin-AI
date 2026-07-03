param(
    [switch]$Force,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$baseUrl = "https://huggingface.co/kftof/bge-reranker-v2-m3-onnx-int8-avx2/resolve/main"
$files = @(
    "model.onnx",
    "tokenizer.json",
    "config.json",
    "special_tokens_map.json",
    "tokenizer_config.json"
)

$targetDirectory = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "bge_rerank_v2"

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
    Write-Host "BGE reranker ONNX files are ready in $targetDirectory"
}
