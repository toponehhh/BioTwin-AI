# Local BGE ONNX files

Both backend applications load local BGE models from this shared solution-level
directory at runtime:

- Embeddings: `LLM/bge_m3`
- Rerank: `LLM/bge_rerank_v2`

## BGE-M3 embeddings

The BGE-M3 model weights are not committed to Git because `bge_m3_model.onnx_data` is larger than 2 GB. Download the required files from Hugging Face:

https://huggingface.co/yuniko-software/bge-m3-onnx/tree/main

Required files:

- `bge_m3_model.onnx`
- `bge_m3_model.onnx_data`
- `bge_m3_tokenizer.onnx`

Place these files in `LLM/bge_m3`.

## BGE rerank

The local reranker loads these files from `LLM/bge_rerank_v2`:

- `model.onnx`
- `tokenizer.json`
- `config.json`
- `special_tokens_map.json`
- `tokenizer_config.json`

Download these files from Hugging Face:

https://huggingface.co/kftof/bge-reranker-v2-m3-onnx-int8-avx2/tree/main

## Windows PowerShell

```powershell
powershell -ExecutionPolicy Bypass -File .\download-bge-m3-onnx.ps1
```

To download the BGE reranker files:

```powershell
powershell -ExecutionPolicy Bypass -File .\download-bge-reranker-v2-m3-onnx.ps1
```

To preview the downloads without writing files:

```powershell
powershell -ExecutionPolicy Bypass -File .\download-bge-m3-onnx.ps1 -DryRun
powershell -ExecutionPolicy Bypass -File .\download-bge-reranker-v2-m3-onnx.ps1 -DryRun
```

## Bash

```bash
bash ./download-bge-m3-onnx.sh
```

To download the BGE reranker files:

```bash
bash ./download-bge-reranker-v2-m3-onnx.sh
```

To preview the downloads without writing files:

```bash
bash ./download-bge-m3-onnx.sh --dry-run
bash ./download-bge-reranker-v2-m3-onnx.sh --dry-run
```

## Publish behavior

The backend project files link this shared directory into publish output. By
default, publish copies only this README and the download scripts to `LLM/` so
the package stays small. To include the local model files in publish output,
publish with:

```powershell
dotnet publish -p:IncludeLocalModels=true
```
