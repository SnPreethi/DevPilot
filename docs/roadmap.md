# DevPilot — Roadmap

Development direction and milestone planning for DevPilot.

---

## Current Status

* Repository indexing — operational
* Local ONNX inference (CPU, DirectML, CUDA) — operational
* Semantic vector search — operational
* VS Code extension integration — operational
* Workflow orchestration and patching — experimental
* Bootstrap and model provisioning automation — operational
* Platform support — Windows only

---

## Phase 1: Core Platform Foundation (Completed)

**Objective:** Establish a working local inference pipeline with repository indexing, semantic search, and basic IDE integration.

* Parallel repository indexer with AST-aware code chunking
* SQLite-backed vector store for local semantic search
* ONNX Runtime integration with DirectML and CPU execution providers
* RAG context orchestration with token-budget-aware prompt assembly
* Local Kestrel REST service exposing indexing, search, and inference endpoints
* VS Code sidebar extension with chat interface

---

## Phase 2: Productization and Stability (Current)

**Objective:** Make the repository cloneable, bootstrappable, and runnable by external developers with minimal friction.

* Automated environment bootstrap script (`bootstrap.ps1`) — validates prerequisites, restores dependencies, installs tooling
* Manifest-driven model download system (`download-models.ps1`) — supports per-variant downloads (CPU, CUDA, DirectML) via Hugging Face CLI
* Model validation script (`validate-models.ps1`) — SHA-256 integrity checks for all ONNX graphs, weights, and tokenizer files
* `.gitignore` configuration to exclude multi-gigabyte model binaries from version control
* CUDA FP16 model variant support alongside existing INT4 CPU/DirectML variants
* Improved error handling and diagnostic output across scripts and service

---

## Phase 3: Workflow Maturity and IDE Expansion (Next)

**Objective:** Harden the workflow engine and expand IDE support beyond VS Code.

* Stabilize multi-step workflow execution and rollback-safe patching
* Improve build failure attribution and automated repair planning
* Expand test coverage across RAG, workflow, and storage subsystems
* Explore Visual Studio 2022 integration via VSIX extension
* Explore Language Server Protocol (LSP) implementation for editor-agnostic support
* Local diagnostics dashboard for indexing performance and inference throughput monitoring

---

## Phase 4: Platform and Intelligence (Future)

**Objective:** Broaden platform support and explore more capable local reasoning.

* Investigate cross-platform execution (macOS via Metal, Linux via ROCm/Vulkan)
* Explore multi-agent workflow coordination for complex multi-file tasks
* Evaluate integration with Microsoft Foundry for optional hybrid local/cloud orchestration
* Investigate Windows ML runtime for OS-level inference scheduling
* Support additional quantized model families beyond Phi-3
* Persistent engineering memory graphs across repositories

---

## Non-Goals

The following are explicitly outside the scope of this project:

* **Cloud-first architecture** — DevPilot is designed to run entirely offline; cloud connectivity may be offered as an opt-in extension, never a requirement.
* **Mandatory telemetry** — no usage data, source code, or queries are transmitted externally.
* **Remote code execution** — all build, test, and patching operations run within the local environment.
* **Autonomous production deployments** — DevPilot assists with code changes but does not autonomously deploy to production systems.
