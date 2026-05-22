# DevPilot — Developer Setup

Instructions for building and running DevPilot from source.

---

## Quick Start

From the repository root:

```powershell
.\scripts\bootstrap.ps1
.\scripts\download-models.ps1
dotnet run --project DevPilot/src/DevPilot.CLI/DevPilot.CLI.csproj service
```

The service starts on `http://localhost:5071`.

---

## Requirements

* **Windows 10 or 11**
* **.NET 8.0 SDK** — [download](https://dotnet.microsoft.com/download)
* **Node.js v18+** — [download](https://nodejs.org)
* **Python 3.9+** — [download](https://python.org) (used by the Hugging Face CLI for model downloads)
* **Git** — [download](https://git-scm.com)
* **VS Code** — [download](https://code.visualstudio.com)

Optional:
* **CUDA Toolkit 12.x + cuDNN 9.x** — only needed if running the CUDA model variant
* A DirectX 12 capable GPU — for the DirectML model variant (no separate install required)

---

## Bootstrap

The bootstrap script validates prerequisites, restores NuGet and npm dependencies, builds the extension bundle, and creates local runtime directories.

```powershell
.\scripts\bootstrap.ps1
```

This runs `validate-prerequisites.ps1` and `setup-dev-env.ps1` internally. You can also run them individually if needed:

```powershell
.\scripts\validate-prerequisites.ps1
.\scripts\setup-dev-env.ps1
```

**Expected result:** All prerequisites pass, NuGet packages restore, npm packages install, and the extension JS bundle is compiled.

---

## Model Setup

ONNX model weights are excluded from Git due to their size. Download them using:

```powershell
.\scripts\download-models.ps1
```

To download a specific variant only:

```powershell
.\scripts\download-models.ps1 -Variant cpu
.\scripts\download-models.ps1 -Variant directml
.\scripts\download-models.ps1 -Variant cuda
```

After downloading, validate file integrity:

```powershell
.\scripts\validate-models.ps1
```

**Expected result:** All model files, weights, and tokenizer assets pass SHA-256 validation.

### Mock Mode (for testing without models)

To run automated tests or UI development without downloading full model weights:

```powershell
$env:DEVPILOT_BOOTSTRAP_MOCK = "true"
.\scripts\download-models.ps1
```

This creates lightweight placeholder files. To remove them:

```powershell
.\scripts\remove-models.ps1
```

---

## Running the Service

Build and start the local REST API service:

```powershell
dotnet build DevPilot
dotnet run --project DevPilot/src/DevPilot.CLI/DevPilot.CLI.csproj service
```

**Expected result:** The Kestrel service starts on `http://localhost:5071` and is ready to accept indexing, search, and inference requests.

---

## VS Code Extension

1. Open the `DevPilot.VSCodeExtension` folder in VS Code.
2. Press **`F5`** to launch the Extension Development Host.
3. Click the **DevPilot icon** in the left Activity Bar.

**Expected result:** The sidebar panel opens with a chat interface connected to the local service.

---

## Tests

Run the backend test suites:

```powershell
dotnet test DevPilot
```

**Expected result:** All xUnit tests pass.

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| Service fails to start | Check that port `5071` is not in use. Verify .NET 8 SDK is installed. |
| Model validation fails | Rerun `.\scripts\download-models.ps1 -Force` to re-download, then `.\scripts\validate-models.ps1`. |
| Extension sidebar shows connection error | Verify the local service is running on port `5071`. |
| ONNX model loading crashes | Ensure you downloaded the correct model variant for your hardware. Run `.\scripts\validate-models.ps1`. |
| `hf` CLI not found | Ensure Python is on PATH. Run `pip install huggingface_hub[cli]` or rerun `.\scripts\bootstrap.ps1`. |
| Extension fails to activate | Reload the Extension Host (`Ctrl+Shift+P` → "Developer: Reload Window"). |

---

## Notes

DevPilot is currently Windows-focused and optimized for local-first development workflows. All inference, indexing, and storage operations run entirely on the local machine.
