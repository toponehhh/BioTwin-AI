# Local BGE ONNX files

The application loads local BGE models from subdirectories at runtime:

- Embeddings: `src/BioTwin_AI/LLM/bge_m3`
- Rerank: `src/BioTwin_AI/LLM/bge_rerank_v2`

## BGE-M3 embeddings

The BGE-M3 model weights are not committed to Git because `bge_m3_model.onnx_data` is larger than 2 GB. Download the required files from Hugging Face:

https://huggingface.co/yuniko-software/bge-m3-onnx/tree/main

Required files:

- `bge_m3_model.onnx`
- `bge_m3_model.onnx_data`
- `bge_m3_tokenizer.onnx`

Place these files in `src/BioTwin_AI/LLM/bge_m3`.

## BGE rerank

The local reranker loads these files from `src/BioTwin_AI/LLM/bge_rerank_v2`:

- `model.onnx`
- `tokenizer.json`
- `config.json`
- `special_tokens_map.json`
- `tokenizer_config.json`

## Windows PowerShell

```powershell
powershell -ExecutionPolicy Bypass -File .\download-bge-m3-onnx.ps1
```

To preview the downloads without writing files:

```powershell
powershell -ExecutionPolicy Bypass -File .\download-bge-m3-onnx.ps1 -DryRun
```

## Bash

```bash
bash ./download-bge-m3-onnx.sh
```

To preview the downloads without writing files:

```bash
bash ./download-bge-m3-onnx.sh --dry-run
```
