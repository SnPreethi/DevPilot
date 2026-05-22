# DevPilot — System Architecture

This document describes the core subsystems inside the DevPilot local service and how they interact.

---

## System Overview

```text
┌──────────────────────────┐
│   VS Code Extension      │  (TypeScript sidebar + webview)
└────────────┬─────────────┘
             │ HTTP / WebSocket
┌────────────▼─────────────┐
│   Local Kestrel Service  │  (Port 5071)
└────────────┬─────────────┘
             │
     ┌───────┼───────┐
     ▼       ▼       ▼
 Indexer   RAG    Workflow
             │       │
     ┌───────┼───────┘
     ▼       ▼
  ONNX    SQLite
 Runtime  Stores
```

The VS Code extension communicates with a local C# Kestrel web service. The service coordinates four main subsystems: the repository indexer, the RAG context orchestrator, the workflow engine, and the local ONNX inference layer. All persistent state is stored in local SQLite databases.

---

## 1. Repository Indexer

The indexer converts a raw directory tree into a structured, searchable codebase representation stored in SQLite.

```text
Filesystem → Parallel Scanner → Symbol Extractor → SQLite DB
```

**How it works:**

* A multi-threaded scanner (`Parallel.ForEachAsync`, throttled to available CPU cores) walks the directory tree and reads source files concurrently.
* Each file is split into semantic code chunks that preserve structural boundaries (classes, methods, namespaces) rather than splitting at arbitrary line counts.
* A symbol extractor parses each chunk to identify definitions, dependencies, and cross-file references.
* Chunks and metadata are written to SQLite through a serialized commit pipeline using `SemaphoreSlim` to avoid SQLite write-lock contention during parallel scans.

**Key files:**

| File | Purpose |
|------|---------|
| `RepositoryIndexingService.cs` | Orchestrates the full indexing pipeline |
| `RepositoryScanner.cs` | Parallel filesystem walker |
| `CodeChunker.cs` | AST-aware code splitting |
| `FileMetadataExtractor.cs` | File-level metadata extraction |

**Project:** `DevPilot.Indexer`

---

## 2. RAG Context Orchestration

The RAG (Retrieval-Augmented Generation) system manages how retrieved code context is assembled and pruned before being sent to the local LLM.

**The problem:**

Quantized models like Phi-3 have limited context windows. Naively appending all retrieval results can push the prompt past the token limit, causing the model to truncate either the system prompt or the user query — both unacceptable.

**The approach:**

DevPilot uses a center-out pruning strategy. Retrieved code blocks are sorted by cosine similarity to the query. Rather than truncating from the end of the prompt (which would cut the user's question), the system removes the lowest-relevance chunks from the middle of the context payload. This keeps both the system instructions and the user query intact.

The orchestrator also integrates diagnostic context (compiler errors, stack traces) and workspace memory (architectural conventions) into the prompt when available.

**Key files:**

| File | Purpose |
|------|---------|
| `ContextOrchestrator.cs` | Main RAG pipeline — retrieval, ranking, pruning, prompt assembly |
| `RagOptimizer.cs` | Token budget calculations and context trimming |
| `DiagnosticAwarePromptBuilder.cs` | Injects compiler/test diagnostics into prompts |
| `RepositoryAwarePromptBuilder.cs` | Adds repository structure context |
| `MemoryAwarePromptBuilder.cs` | Includes persistent workspace conventions |
| `SimpleRagPipeline.cs` | Lightweight retrieval path for simple queries |

**Projects:** `DevPilot.RAG`, `DevPilot.AI`

---

## 3. Workflow Engine

The workflow engine drives multi-step code operations such as refactoring plans, modernization tasks, and automated patching sequences.

**How it works:**

* A planner decomposes a high-level task (e.g. "migrate deprecated API calls") into a directed graph of individual steps.
* Each step can be executed, validated against the local compiler, and rolled back if the build breaks.
* Active workflow states, approval checkpoints, and task graphs are persisted to local storage so that interrupted sessions can resume from the last successful step.

The patching subsystem uses a search-replace engine that applies edits to source files and validates the result by running a local build. If compilation fails, edits are reverted automatically.

**Key files:**

| File | Purpose |
|------|---------|
| `TaskGraphOrchestrator.cs` | Executes task graphs with ordering and dependency tracking |
| `EngineeringWorkflowPlanner.cs` | Decomposes tasks into step sequences |
| `WorkflowTaskStateMachine.cs` | Tracks per-step state transitions |
| `ModernizationEngine.cs` | Drives codebase modernization plans |
| `SearchReplacePatchEngine.cs` | Applies and validates search-replace edits |
| `SQLiteWorkflowStateStore.cs` | Persists workflow state to SQLite |

**Projects:** `DevPilot.Core/Workflow`, `DevPilot.Core/Modernization`, `DevPilot.Patching`, `DevPilot.Storage`

---

## 4. Local ONNX Inference

All AI inference runs locally via ONNX Runtime. No external API calls are made.

**Execution providers:**

The system selects a hardware backend at startup based on what is available:

1. **DirectML** — used on Windows machines with a DirectX 12 capable GPU
2. **CUDA** — used when an NVIDIA GPU with CUDA 12.x is detected
3. **CPU** — fallback when no compatible GPU is present

The `OnnxSessionFactory` configures ONNX Runtime session options (intra-op/inter-op thread counts, memory allocation patterns) scaled to the local machine's core count. Thread allocation is capped to prevent inference workloads from starving the IDE and other developer tools of CPU time.

**Two model pipelines:**

* **Embedding pipeline** (`OnnxEmbeddingService.cs`): Runs `all-MiniLM-L6-v2` to generate vector representations of code chunks for semantic search.
* **Generation pipeline** (`OnnxLLMService.cs`): Runs quantized `Phi-3-mini` for chat, explanation, refactoring, and code generation tasks with streaming token output.

**Key files:**

| File | Purpose |
|------|---------|
| `OnnxSessionFactory.cs` | Configures ONNX Runtime sessions and execution providers |
| `ExecutionProviderSelector.cs` | Detects available hardware and selects DirectML / CUDA / CPU |
| `OnnxEmbeddingService.cs` | Embedding generation pipeline |
| `OnnxLLMService.cs` | LLM inference with streaming token output |
| `InferenceProfiler.cs` | Measures inference latency and throughput |

**Project:** `DevPilot.AI`

---

## 5. Storage Layer

All persistent state is stored in local SQLite databases. There is no remote database dependency.

**Stores:**

| Store | Purpose |
|-------|---------|
| `SQLiteChunkStore` | Indexed code chunks |
| `SQLiteEmbeddingStore` | Vector embeddings for semantic search |
| `SQLiteSymbolStore` | Extracted symbol definitions and references |
| `SQLiteGraphStore` | Cross-file relationship graphs |
| `SQLiteVectorStore` | Raw vector data for similarity queries |
| `SQLiteWorkflowStateStore` | Active workflow and modernization states |
| `SQLiteWorkspaceMemoryStore` | Persistent architectural conventions |
| `SQLiteExecutionPipelineStore` | Execution pipeline history and state |

The `SqliteConnectionFactory` manages connection lifecycle and ensures WAL (Write-Ahead Logging) mode is enabled for concurrent read performance.

**Project:** `DevPilot.Storage`
