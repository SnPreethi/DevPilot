# *DEVPILOT*

> A local AI coding assistant that understands codebase, analyzes failures, and helps automate real development workflows — entirely on local machine.

---

## What is DevPilot?

DevPilot is an experimental local AI developer assistant built to run completely offline.

Instead of relying on LLMs using cloud services, DevPilot runs:

- local ONNX models
- repository indexing
- semantic code search
- diagnostics analysis
- workflow orchestration
- code patch generation
- execution-aware reasoning

entirely on the local machine.

The project started from a simple idea:

> "What if an AI coding assistant could understand a repository, run builds/tests locally, and safely coordinate engineering workflows without internet and without sending proprietary code to external servers?"

DevPilot is an attempt to explore that idea.

---

## Why I Built This

Most coding assistants today are optimized for prompt completion.

They are extremely useful, but they usually:

- depend on remote APIs
- cannot access local execution environments safely
- have limited repository awareness
- cannot coordinate multi-step engineering workflows
- struggle with large codebases and diagnostics context

Wanted to experiment with a different direction:

- fully local inference
- repository-aware reasoning
- workflow orchestration
- execution-aware debugging
- rollback-safe patching
- persistent engineering memory

DevPilot is designed as a systems-oriented engineering assistant rather than only a chat interface.

---

## Current Capabilities

### Repository Intelligence

- Parallel repository indexing
- AST-aware code chunking
- Symbol-aware metadata extraction
- Cross-file relationship mapping
- Repository graph orchestration
- Semantic vector search over local codebases

### Local AI Inference

- ONNX Runtime execution
- DirectML GPU acceleration
- CPU fallback support
- Local embedding generation
- Local Phi-3 inference pipeline
- Streaming token responses

### Developer Workflows

- Engineering workflow orchestration
- Task graph execution
- Approval checkpoints
- Dry-run validation
- Rollback-safe patching
- Execution pipelines
- Persistent workflow state

### IDE Integration

- VS Code sidebar integration
- Inline completions
- Diagnostics-aware prompts
- Terminal failure analysis
- Quick-fix generation
- Selection-aware reasoning

### Local Execution Awareness

- Build failure parsing
- Stack trace analysis
- Test execution awareness
- Runtime diagnostics
- Context-aware repair planning

### Persistent Workspace Memory

- SQLite-backed memory store
- Architectural convention tracking
- Repository modernization memory
- Persistent execution history

---

## Technology Stack

* **Language & Core Runtime:** C# 12 (.NET 8.0 SDK)
* **Local AI Runtimes:** ONNX Runtime
* **Hardware Acceleration:** DirectML, CUDA, CPU based on the available hardware.
* **Local Database:** SQLite.
* **IDE Integration:** VS Code extension API
* **Extension Frontend:** TypeScript + Webviews.
* **Desktop UI:** WinUI 3
* **Embedding Models:** all-MiniLM-L6-v2.
* **Local Generative LLM:** Phi-3-Mini

---

## Architecture Overview

DevPilot is split into several independent subsystems.

```text
┌─────────────────────────────────────────────────────────┐
│              VS Code Extension Development Host         │
│  (TypeScript Sidebar UI Panel + Webview WebView Chats)  │
└───────────┬─────────────────────────────────▲───────────┘
            │ HTTP REST / JSON API Calls      │ WebSockets / Events
┌───────────▼─────────────────────────────────┴───────────┐
│              DevPilot CLI / REST Service                │
│  (Local Kestrel REST Web API Service on Port 5071)      │
└───────────┬─────────────────────────────────────────────┘
            ├──────────────────────┬──────────────────────┐
┌───────────▼──────────┐ ┌─────────▼──────────┐ ┌─────────▼──────────┐
│ Repository Indexer   │ │  Workflows Engine  │ │ Local AI Service   │
│ (Parallel scanner,   │ │ (Task orchestration│ │ (ONNX Runtime,     │
│  symbol scopes)      │ │  rollback patches) │ │  DirectML / CPU)   │
└───────────┬──────────┘ └─────────┬──────────┘ └─────────┬──────────┘
┌───────────▼──────────────────────▼──────────────────────▼──────────┐
│                   Local SQLite Database Stores                     │
│ (Vector embeddings, file indexes, persistent modernization states) |
└────────────────────────────────────────────────────────────────────┘
```

The system is intentionally designed around:

- offline-first execution
- modular orchestration
- deterministic workflows
- local persistence
- low external dependencies

---

## Repository Structure

```
├── DevPilot/                        # Primary .NET C# Backend Engine codebase
│   ├── src/                         # Backend projects (AI, Indexer, RAG, Storage, UI, CLI)
│   ├── tests/                       # Complete xUnit Test Suites (Core, Storage, AI, RAG)
│   ├── data/                        # [Local] SQLite active workspace storage
│   ├── models/                      # [Local] Quantized ONNX weights & manifest configurations
│   ├── cache/                       # [Local] Operational runtime state maps
│   └── logs/                        # [Local] Execution and Diagnostics logs
├── DevPilot.VSCodeExtension/        # TypeScript VS Code sidebar & chat panels
├── scripts/                         # Unified installer, setup, and model provisioning scripts
├── docs/                            # Deep-dive system documentation guides
└── README.md                        # Master onboarding documentation
```

---

## Quick Start

### Prerequisites
Ensure you have the following installed on your Windows machine:
* Windows 10 or 11
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* [Node.js v18.x](https://nodejs.org)
* [VS Code](https://code.visualstudio.com)
* [Git](https://git-scm.com)

Optional:
* If the system has a NVIDIA GPU or DirectX 12 capable GPU, then please set up [CUDA](https://developer.nvidia.com/cuda) or [DirectML](https://docs.microsoft.com/en-us/windows/ai/directml/) for acceleration. (Recommended)
* [Visual Studio 2022](https://visualstudio.microsoft.com)

---

### 1. Clone the Repository

```powershell
git clone https://github.com/SnPreethi/DevPilot.git
cd DevPilot
```

---

### 2. Bootstrap the Workspace

```powershell
.\scripts\bootstrap.ps1
```

This restores:

- NuGet dependencies
- Node dependencies
- extension tooling
- local build environment

---

### 3. Download Local Models

```powershell
.\scripts\download-models.ps1
```

This downloads:

- all-MiniLM-L6-v2 embeddings
- Phi-3 Mini ONNX weights
- tokenizer assets
- runtime configs

---

### 4. Validate Runtime

```powershell
.\scripts\validate-models.ps1
```

---

### 5. Start the Local Service

```powershell
dotnet run --project .\DevPilot\src\DevPilot.CLI\DevPilot.CLI.csproj service
```

---

### 6. Launch the VS Code Extension

```powershell
cd DevPilot.VSCodeExtension
code .
```

Press:

```text
F5
```

This launches a sandboxed VS Code Extension Host.

---

## Example Workflow

A typical DevPilot workflow looks like this:

1. Index a repository
2. Extract repository symbols and dependencies
3. Run semantic search over indexed code
4. Detect compiler/test/runtime failures
5. Build repository-aware prompts
6. Generate edit plans
7. Validate patches in dry-run mode
8. Apply approved changes
9. Run rollback-safe execution pipelines
10. Persist workflow memory for future reasoning

---

## Offline-First Philosophy

DevPilot is intentionally designed to run locally.

By default:

- no source code is uploaded
- no telemetry is sent externally
- no cloud inference APIs are required
- embeddings remain local
- workflow history remains local
- vector databases remain local

All inference and orchestration execute on one's own machine.

---

## Current Limitations

This project is still experimental.

Current limitations include:

- Windows-focused runtime support
- limited model compatibility
- partial WinUI packaging instability on some environments
- no Linux/macOS support yet
- large local model footprint
- evolving orchestration system
- inference quality depends heavily on local hardware

This is not intended to compete with commercial coding assistants.

It is primarily:

- a systems engineering experiment
- a local AI tooling exploration
- a repository orchestration research project

---

## Roadmap

### Near-Term

- Better execution pipeline coordination
- Smarter repository graph traversal
- Improved patch planning
- More stable WinUI packaging
- Faster indexing
- Better diagnostics attribution

### Mid-Term

- Multi-agent orchestration
- Smarter task delegation
- Persistent repository memory graphs
- Autonomous modernization pipelines

### Long-Term

- Visual Studio integration
- Multi-repository coordination
- Distributed local inference
- Enterprise offline deployment support

---

## Documentation

Additional documentation is available inside the `docs/` directory.

Suggested reading order:

- `docs/architecture.md`
- `docs/setup.md`
- `docs/workflows.md`
- `docs/troubleshooting.md`
- `docs/demo-guide.md`
- `docs/roadmap.md`

---

# Project Status

Current state:

- Core platform implemented
- Local inference pipeline operational
- Repository indexing operational
- Workflow orchestration operational
- VS Code integration operational
- Packaging automation operational
- Still under active experimentation and iteration

---

# Final Note

DevPilot is ultimately an experiment around a simple question:

> "What becomes possible when AI-assisted software engineering is designed around local execution, repository awareness, and workflow orchestration instead of only chat completions?"

The project is still evolving, but the architecture foundation is now in place.