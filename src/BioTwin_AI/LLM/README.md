# Local BGE-M3 ONNX files

The application loads BGE-M3 embeddings from this directory at runtime.

The model weights are not committed to Git because `bge_m3_model.onnx_data` is larger than 2 GB. Download the required files from Hugging Face:

https://huggingface.co/yuniko-software/bge-m3-onnx/tree/main

Required files:

- `bge_m3_model.onnx`
- `bge_m3_model.onnx_data`
- `bge_m3_tokenizer.onnx`

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
