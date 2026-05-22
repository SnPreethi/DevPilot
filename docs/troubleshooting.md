# DevPilot — Troubleshooting

Common runtime, setup, and extension issues encountered during local development.

---

## Log and State Locations

| Path | Contents |
|------|----------|
| `DevPilot/logs/` | Runtime and diagnostics logs |
| `DevPilot/data/` | SQLite databases (indexes, embeddings, workflow state) |
| `DevPilot/models/` | ONNX model weights, tokenizer files, manifests |
| `DevPilot/cache/` | Temporary runtime caches and download staging |

---

## ONNX Model Loading Failures

### Symptom
`FileNotFoundException` when the service attempts to load a model.

### Cause
Model weight files (`model.onnx`, `model.onnx.data`) or tokenizer files are missing from `DevPilot/models/`.

### Remediation

Redownload the model assets:

```powershell
.\scripts\download-models.ps1
```

Then validate file integrity:

```powershell
.\scripts\validate-models.ps1
```

For development without full models, use mock mode:

```powershell
$env:DEVPILOT_BOOTSTRAP_MOCK = "true"
.\scripts\download-models.ps1
```

---

## Model Download Failures

### Symptom
`download-models.ps1` fails partway through, or `validate-models.ps1` reports checksum mismatches.

### Cause
Network interruption during Hugging Face CLI download, or partially written files from a previous attempt.

### Remediation

1. Delete the partial model folder:
   ```powershell
   Remove-Item -Recurse -Force DevPilot/models/llm
   ```
2. Redownload with the force flag:
   ```powershell
   .\scripts\download-models.ps1 -Force
   ```
3. Validate:
   ```powershell
   .\scripts\validate-models.ps1
   ```

If the `hf` CLI itself is missing, install it:

```powershell
pip install huggingface_hub[cli]
```

---

## DirectML / GPU Fallback to CPU

### Symptom
Inference runs on CPU despite a GPU being present. Service logs show no DirectML or CUDA provider loaded.

### Cause
The machine lacks a DirectX 12 compatible GPU, GPU drivers are outdated, or (for CUDA) the CUDA Toolkit is not installed.

### Remediation

* Update your GPU drivers to the latest version.
* For CUDA: install [CUDA Toolkit 12.x](https://developer.nvidia.com/cuda-downloads) and [cuDNN 9.x](https://developer.nvidia.com/cudnn).
* If no compatible GPU is available, the service falls back to CPU inference automatically. This is expected behavior — inference will be slower but functional.

---

## SQLite Database Locked

### Symptom
`SQLite Error 5: database is locked`

### Cause
Concurrent database writes from the indexer and workflow engine.

### Remediation

The connection factory configures WAL mode and a busy timeout to handle typical concurrency. If locks persist:

1. Stop all running tasks.
2. Restart the service:
   ```powershell
   dotnet run --project DevPilot/src/DevPilot.CLI/DevPilot.CLI.csproj service
   ```

If the database is corrupted, reset it (see [Reset Local Runtime State](#reset-local-runtime-state) below).

---

## Service Connectivity Issues

### Symptom
VS Code sidebar shows a connection error, or API requests to `http://localhost:5071` fail.

### Cause
The local service is not running, or port `5071` is occupied by another process.

### Remediation

Verify the service is running:

```powershell
dotnet run --project DevPilot/src/DevPilot.CLI/DevPilot.CLI.csproj service
```

Check if port `5071` is already in use:

```powershell
netstat -ano | findstr :5071
```

If another process is holding the port, terminate it or restart your machine.

---

## VS Code Extension Issues

### Symptom
`View container 'devpilot-sidebar' does not exist` warning, or the sidebar icon does not appear.

### Cause
The extension bundle was not compiled, or the icon asset is missing.

### Remediation

Rebuild the extension:

```powershell
cd DevPilot.VSCodeExtension
npm install
npm run package
```

If the extension loads but the sidebar is unresponsive, reload the Extension Host:

`Ctrl+Shift+P` → "Developer: Reload Window"

---

## Reset Local Runtime State

If persistent issues remain after the above steps, reset all local databases, caches, and runtime state:

```powershell
.\scripts\reset-runtime.ps1
```

This clears:
* SQLite databases in `DevPilot/data/`
* Runtime caches in `DevPilot/cache/`
* Persistent workflow state

After resetting, re-index your workspace and restart the service.
