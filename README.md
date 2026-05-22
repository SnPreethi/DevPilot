# *DEVPILOT*

Local-first development assistant for repository indexing, semantic search, and workflow-aware code analysis on Windows.

[![.NET](https://img.shields.io/badge/.NET-8.0-blue)](https://dotnet.microsoft.com/download)
[![TypeScript](https://img.shields.io/badge/TypeScript-5.0-blue)](https://www.typescriptlang.org/)

DevPilot is an experimental local development assistant designed to run entirely on one's machine. It combines repository indexing, semantic search, ONNX-based local inference, diagnostics analysis, and workflow execution into a single offline-capable system without requiring remote LLM APIs.

---

## WHY DEVPILOT EXISTS

Most AI coding assistants rely on remote hosted models. While effective, that architecture introduces practical limitations for local development workflows:

- Proprietary repositories may not be allowed outside local environments.
- Cloud APIs do not have direct visibility into local build state, test execution, or runtime diagnostics.
- Network availability and API latency interrupt local engineering workflows.

DevPilot explores a different approach:

- local inference
- repository-aware indexing
- local workflow execution
- diagnostics-aware reasoning
- developer-supervised patch workflows

The project started from a simple idea:

> What if a local development assistant could understand a repository, analyze failures, and coordinate safe engineering workflows without sending source code to external services?

---

## CURRENT CAPABILITIES

### Repository Intelligence

- Parallel repository indexing
- AST-aware code chunking
- Symbol-aware metadata extraction
- Cross-file relationship mapping
- Semantic vector search over local repositories

### Local Inference

- ONNX Runtime execution
- DirectML acceleration
- CUDA acceleration
- CPU fallback support
- Local embedding generation
- Phi-3 ONNX inference pipeline
- Streaming token responses

### Workflow Execution

- Validation-gated execution pipelines
- Dry-run workflow validation
- Rollback-aware patch execution
- Persistent workflow checkpoints
- Diagnostics-aware repair planning

### IDE Integration

- VS Code sidebar extension
- Selection-aware repository reasoning
- Terminal failure analysis
- Interactive relationship viewer
- Context-aware repository prompts

### Runtime Awareness

- Build failure parsing
- Stack trace analysis
- Test execution awareness
- Runtime diagnostics collection
- Local execution context tracking

---

## ARCHITECTURE OVERVIEW

DevPilot separates the interface layer from the local execution runtime.

```text
VS Code Extension
        ↓
Local Service (Kestrel)
        ↓
Repository Indexing + Workflow Engine
        ↓
SQLite + ONNX Runtime
        ↓
Local Models (Embeddings + Phi-3)
```

The VS Code extension acts as a lightweight client that communicates with a local service running on port `5071`.

### Core Components

- **Local Service** — Coordinates indexing, workflows, diagnostics, and inference.
- **Repository Indexer** — Builds repository-aware semantic metadata inside SQLite.
- **Workflow System** — Executes validation-aware patch workflows and rollback checkpoints.
- **Local AI Runtime** — Loads embedding and generative ONNX models locally through ONNX Runtime.

<br>

<img src="assets/devpilot_architecture_visual.png" width="500" alt="DevPilot Architecture"/>


### VS Code Sidebar Integration

The TypeScript extension provides repository-aware interactions directly inside VS Code.

<img src="assets/devpilot_UI.png" width="800" alt="DevPilot Interface" />

### Workflow Execution Pipelines

Execution pipelines validate repository state before applying modifications.

<img src="assets/devpilot_workflow_pipeline.png" width="500" alt="Workflow Execution Pipeline" />

---

## TECHNOLOGY STACK

* **Backend**: .NET 8, ASP.NET Core, SQLite
* **AI Runtime**: ONNX Runtime, DirectML, Phi-3-mini
* **IDE Integration**: VS Code Extension API, TypeScript
* **Desktop UI**: WinUI 3
* **Tooling**: PowerShell, MSBuild, npm

---

## QUICK START

### 1. Prerequisites

- Windows 10 or 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Node.js v18+](https://nodejs.org)
- [Python 3.9+](https://python.org)
- [VS Code](https://code.visualstudio.com)
- [Git](https://git-scm.com)

Optional:

- NVIDIA CUDA Toolkit 12.x + cuDNN 9.x (CUDA model variant)
- DirectX 12 compatible GPU (DirectML acceleration)

> [!NOTE]
> The CPU model variant works out of the box with no additional GPU setup. CUDA and DirectML are only needed if you choose to download those specific model variants via `.\scripts\download-models.ps1 -Variant cuda` or `-Variant directml`.

After ensuring the above prerequisites are met, run the following commands in PowerShell.


### 2. Clone the Repository

```powershell
git clone https://github.com/SnPreethi/DevPilot.git
cd DevPilot
```

### 3. Bootstrap the Workspace

```powershell
.\scripts\bootstrap.ps1
```

This restores:

- .NET dependencies
- Node dependencies
- VS Code extension assets
- local runtime directories


### 4. Provision Local AI Models

```powershell
# Download all model variants (CPU, CUDA, DirectML)
.\scripts\download-models.ps1

# Download specific model variant
.\scripts\download-models.ps1 -Variant cpu
```

### 5. Validate Runtime Assets

```powershell
.\scripts\validate-models.ps1
```

This script ensures all downloaded ONNX models, graphs, weights, and tokenizer files are intact and verified.

### 6. Start the Local Service

```powershell
dotnet run --project DevPilot/src/DevPilot.CLI/DevPilot.CLI.csproj service
```

Expected result:

- Local service starts on port `5071`
- Runtime validation completes successfully
- VS Code extension can connect locally

### 7. Launch the VS Code Extension

1. Open `DevPilot.VSCodeExtension` folder in VS Code.
2. Press `F5` to launch the sandboxed Extension Host.
3. Open the DevPilot side-panel from the Activity Bar in the left.


> **NOTE** - To begin indexing your codebase, running semantic queries, and executing interactive workflows, please refer to the step-by-step guides in the ***docs/*** directory.

---

## REPOSITORY STRUCTURE

```text
├── DevPilot/
│   ├── src/                # Backend services and runtime components
│   ├── tests/              # xUnit test suites
│   ├── models/             # Local ONNX models and manifests
│   ├── data/               # SQLite runtime databases
│   ├── cache/              # Runtime caches
│   └── logs/               # Local diagnostics logs
│
├── DevPilot.VSCodeExtension/   # VS Code extension client
├── scripts/                    # Bootstrap and provisioning scripts
├── docs/                       # Architecture and operational guides
└── README.md
```

---

## OFFLINE-FIRST PHILOSOPHY

DevPilot is designed to operate entirely on the local machine.

- Repository data remains local.
- Semantic indexing runs locally.
- ONNX inference executes locally.
- SQLite persistence remains local.
- No cloud APIs are required.

This allows:

- offline operation
- lower latency
- repository privacy
- reproducible local workflows

---

## CURRENT LIMITATIONS

DevPilot is still experimental and under active development.

Current constraints include:

- Windows-focused runtime and packaging
- Large local model storage requirements
- Variable inference performance depending on hardware
- Experimental workflow and rollback systems
- Evolving orchestration abstractions

---

## ROADMAP

### Current Focus

- Improve indexing stability for large repositories
- Expand runtime validation coverage
- Improve workflow rollback reliability
- Reduce ONNX cold-start latency
- Improve test coverage across runtime components
- Stabilize WinUI packaging and installer workflows

### Planned Integrations

- Visual Studio integration
- Language Server Protocol (LSP) support
- Optional Azure AI Foundry hybrid workflows
- Windows ML runtime exploration
- Additional ONNX model families

### Long-Term Exploration

- Cross-platform runtime support
- Distributed local inference
- Expanded repository relationship analysis
- Multi-agent workflow coordination experiments

---

## DOCUMENTATION

Additional documentation is available in the `docs/` directory:

- `architecture.md`
- `setup.md`
- `demo-guide.md`
- `troubleshooting.md`
- `workflows.md`
- `roadmap.md`

---