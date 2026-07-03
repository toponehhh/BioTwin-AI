#!/usr/bin/env bash
set -euo pipefail

dry_run=0
force=0

for arg in "$@"; do
  case "$arg" in
    --dry-run)
      dry_run=1
      ;;
    --force)
      force=1
      ;;
    *)
      echo "Unknown argument: $arg" >&2
      echo "Usage: $0 [--dry-run] [--force]" >&2
      exit 2
      ;;
  esac
done

base_url="https://huggingface.co/yuniko-software/bge-m3-onnx/resolve/main"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
target_dir="${script_dir}/bge_m3"
files=(
  "bge_m3_model.onnx"
  "bge_m3_model.onnx_data"
  "bge_m3_tokenizer.onnx"
)

for file in "${files[@]}"; do
  url="${base_url}/${file}"
  destination="${target_dir}/${file}"

  if [[ "$dry_run" == "1" ]]; then
    echo "Would download ${url} -> ${destination}"
    continue
  fi

  mkdir -p "$target_dir"

  if [[ -f "$destination" && "$force" != "1" ]]; then
    echo "Skipping existing file: ${destination}"
    continue
  fi

  temporary_file="${destination}.download"
  rm -f "$temporary_file"

  echo "Downloading ${file}..."
  curl --fail --location --retry 3 --output "$temporary_file" "$url"

  if [[ ! -s "$temporary_file" ]]; then
    rm -f "$temporary_file"
    echo "Downloaded file is empty: ${file}" >&2
    exit 1
  fi

  mv -f "$temporary_file" "$destination"
  echo "Saved ${destination}"
done

if [[ "$dry_run" == "1" ]]; then
  echo "Dry run complete. No files were downloaded."
else
  echo "BGE-M3 ONNX files are ready in ${target_dir}"
fi
