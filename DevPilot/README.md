# DevPilot for Windows

DevPilot for Windows is a Windows-native, offline-first foundation for a local semantic code search and AI developer assistant.

This repository contains the production-grade backend foundation, repository indexing pipeline, local embedding persistence, semantic search pipeline, simple grounded RAG assistant, diagnostics tooling, and the first WinUI 3 desktop shell. It intentionally does not implement advanced chat memory, agents, Windows ML optimization, Foundry orchestration, or cloud integrations.

## Stack

- C# and .NET 8
- SQLite local storage for repositories, files, chunks, and embeddings
- ONNX Runtime embedding service
- ONNX Runtime local LLM service foundation
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Configuration
- Microsoft.Extensions.Logging
- System.CommandLine CLI shell
- WinUI 3 and Windows App SDK desktop shell
- CommunityToolkit.Mvvm for ViewModels and commands
- xUnit test scaffolding

Future phases are expected to add Microsoft Foundry Local, Windows ML optimization, WinUI 3, and Windows App SDK integration.

## Structure

- `src/DevPilot.Contracts`: shared DTOs, interfaces, and service contracts
- `src/DevPilot.Core`: settings, constants, and core DI registration
- `src/DevPilot.Indexer`: indexing boundary registration
- `src/DevPilot.Storage`: SQLite connection and initialization foundation
- `src/DevPilot.AI`: local AI boundary registration
- `src/DevPilot.RAG`: retrieval and prompt orchestration boundary registration
- `src/DevPilot.LocalService`: background service shell
- `src/DevPilot.CLI`: command-line host and bootstrap
- `src/DevPilot.UI`: WinUI 3 desktop shell, MVVM ViewModels, and UI-facing facade services
- `tests`: xUnit scaffolding
- `models`, `prompts`, `scripts`, `installer`, `docs`, `architecture`: placeholders for future assets

## Build

```powershell
dotnet restore .\DevPilot.sln
dotnet build .\DevPilot.sln
dotnet test .\DevPilot.sln
```

## CLI

```powershell
dotnet run --project .\src\DevPilot.CLI -- index .
dotnet run --project .\src\DevPilot.CLI -- search "where is configuration loaded?" --top 5
dotnet run --project .\src\DevPilot.CLI -- debug-search "where is JWT validation?" --top 5
dotnet run --project .\src\DevPilot.CLI -- inspect-prompt "explain auth flow" --top 5
dotnet run --project .\src\DevPilot.CLI -- inspect-chunks --file AuthService.cs
dotnet run --project .\src\DevPilot.CLI -- ask "summarize this repository" --top 5
```

`index` scans a local repository, filters supported files, extracts file metadata, chunks source content, persists repository/file/chunk records into SQLite, and generates local chunk embeddings when enabled. Indexing is incremental: unchanged files are skipped by SHA256 hash, deleted files are removed from SQLite, changed files update only their current chunks, and stale embeddings are regenerated based on chunk hash plus embedding version settings.

`search` generates a local query embedding, performs local cosine retrieval against persisted vectors, and prints ranked chunks.

`debug-search` prints retrieval diagnostics: query embedding timing, retrieval timing, ranked chunks, similarity scores, line ranges, chunk IDs, and token estimates.

`inspect-prompt` retrieves context and renders the grounded prompt without running inference. It reports retrieved chunk count, approximate prompt tokens, retrieval timing, and prompt build timing.

`inspect-chunks` prints persisted chunk boundaries, symbol names, line ranges, character counts, token estimates, and chunk hashes. Use `--file` to filter by file name or path.

`ask` retrieves semantic context, builds a grounded prompt, runs the local LLM service, and prints an answer with referenced files.

## Desktop App

`DevPilot.UI` is the first Windows-native desktop experience. It uses a thin MVVM layer:

```text
Views -> ViewModels -> UI Application Services -> Existing Backend Services -> SQLite / Local Models
```

The shell uses `NavigationView` and `Frame` navigation with pages for:

- Repositories: add/index/remove repositories and view file/chunk counts.
- Search: run semantic search and inspect ranked local results.
- Assistant: ask grounded questions and inspect referenced files/context.
- Diagnostics: inspect retrieval matches, prompt preview, token estimates, timings, and chunk statistics.
- Settings: view local model paths, retrieval limits, prompt limits, offline status, and runtime information.

The UI does not contain embedding, retrieval, prompt, inference, chunking, or SQLite logic. ViewModels call facade services in `DevPilot.UI.Services`, and those facades delegate to the existing backend contracts.

## Model Layout

Expected local model layout:

```text
models/
  embeddings/
    all-MiniLM-L6-v2/
      model.onnx
  llm/
    phi-3-mini/
      model.onnx
```

Supported file extensions:

- `.cs`
- `.ts`
- `.js`
- `.py`
- `.java`
- `.md`
- `.json`
- `.yaml`
- `.yml`

Ignored directories include `.git`, `node_modules`, `bin`, `obj`, `dist`, `build`, `packages`, and `.vs`.

## Current Limits

- ONNX Runtime integration is present, but you must place the local all-MiniLM-L6-v2 ONNX model at `models/embeddings/all-MiniLM-L6-v2/model.onnx` to use real model inference.
- Phi-3 local LLM config expects `models/llm/phi-3-mini/model.onnx`.
- If the model file is missing and `AllowDeterministicFallback` is true, DevPilot uses deterministic local fallback embeddings for offline development and tests.
- If the Phi-3 model is missing and `LLM:AllowExtractiveFallback` is true, `ask` returns a grounded extractive answer from retrieved context.
- Embeddings are stored in SQLite in the `Embeddings` table.
- Embeddings include `EmbeddingModelVersion`, `EmbeddingSchemaVersion`, `ChunkHash`, and `IndexedAtUtc` so future model upgrades can invalidate and regenerate stale vectors without rebuilding the whole repository.
- sqlite-vss can be enabled by setting `VectorSearch:UseSqliteVss` and `VectorSearch:SqliteVssExtensionPath`; otherwise retrieval uses local cosine ranking over SQLite-stored vectors.
- No conversational memory, tool calling, agents, or cloud inference exists.
- C# chunking uses Roslyn for class, interface, and method boundaries.
- Markdown chunking splits by headings.
- Other supported languages use simple line-based chunks.
