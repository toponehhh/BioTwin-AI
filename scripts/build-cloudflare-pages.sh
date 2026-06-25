#!/usr/bin/env sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
CONFIGURATION="${CONFIGURATION:-Release}"
OUTPUT_PATH="${OUTPUT_PATH:-artifacts/cloudflare-pages}"

cd "$ROOT"
"$DOTNET" publish ./src/BioTwin_AI.BlazorClient/BioTwin_AI.BlazorClient.csproj \
  -c "$CONFIGURATION" \
  -o "$OUTPUT_PATH" \
  -p:UseAppHost=false

printf '%s\n' "Cloudflare Pages artifact: $ROOT/$OUTPUT_PATH/wwwroot"
