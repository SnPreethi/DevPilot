# Module Responsibilities

## DevPilot.Contracts

Defines shared DTOs and interfaces used across the solution. This project should remain free of implementation logic.

## DevPilot.Core

Owns application settings, constants, core dependency injection setup, approximate token estimation utilities, deterministic execution context orchestration, execution pipeline orchestration, workspace memory orchestration, and approval-aware engineering workflow planning primitives.

## DevPilot.Indexer

Owns repository scanning, supported file detection, metadata extraction, source chunking, indexing orchestration, incremental hash comparison, deleted-file cleanup, and chunk inspection. C# chunking uses Roslyn, Markdown splits by headings, and other supported languages use simple line-based chunks.

## DevPilot.Storage

Owns SQLite connection creation, WAL-enabled schema initialization, persistence for repositories, file metadata, chunks, embeddings, embedding version columns, workflow instances, workflow task graphs, workflow dependencies, workflow execution history, execution pipelines, execution stages, checkpoints, artifacts, failures, validations, rollback snapshots, timeline events, and local vector search over persisted vectors.

## DevPilot.AI

Owns local embedding services, lazy ONNX embedding and LLM loading, tokenization abstraction, embedding pipeline orchestration, embedding invalidation checks, semantic search, retrieval diagnostics, model validation, and the local LLM service. It does not implement conversational memory or agents.

## DevPilot.RAG

Owns grounded prompt construction, prompt inspection, and the simple RAG pipeline. It orchestrates semantic retrieval, prompt building, local inference, and assistant response formatting.

## DevPilot.LocalService

Provides the long-running localhost orchestration entry point for desktop, CLI, and VS Code hosts. It exposes local endpoints for runtime status, search, chat, diagnostics, execution analysis, memory events, edit preview/apply workflows, deterministic workflow state coordination, and supervised execution pipeline coordination.

## DevPilot.CLI

Bootstraps configuration, logging, dependency injection, and commands. The `index` command runs incremental repository indexing and embedding generation. The `search` command performs local semantic retrieval. The `debug-search` command inspects rankings and timings. The `inspect-prompt` command renders grounded prompts without inference. The `inspect-chunks` command displays chunk boundaries and token estimates. The `ask` command runs the simple grounded RAG pipeline.

## DevPilot.UI

Owns the WinUI 3 desktop shell, `NavigationView`/`Frame` navigation, MVVM ViewModels, and UI-facing facade services. Views and ViewModels remain thin: they do not implement retrieval, embeddings, prompt construction, inference, chunking, or SQLite logic. UI facades call existing backend services and map backend DTOs into bindable UI models.

## Current Indexing Limits

- Real ONNX embedding inference requires a local model file at `models/embeddings/all-MiniLM-L6-v2/model.onnx`.
- Real Phi-3 inference requires a local model file at `models/llm/phi-3-mini/model.onnx`.
- sqlite-vss requires a local extension path and is optional in this foundation; local cosine ranking is available without external services.
- `ask` currently has no conversation memory and returns one grounded answer per command invocation.
- Ignore rules and language detection are extension/configuration based.
- Chunking is intentionally simple and deterministic for the foundation phase.
