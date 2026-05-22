# DevPilot — Demo Guide

A practical walkthrough for demonstrating the core features of DevPilot.

---

## Recommended Demo Order

1. Repository Indexing
2. Semantic Code Search
3. VS Code Sidebar Chat
4. Code Patching and Validation

---

## Before You Start

Ensure the following are ready before running any demo scenario:

- [ ] Models downloaded — run `.\scripts\download-models.ps1` from the repo root
- [ ] Models validated — run `.\scripts\validate-models.ps1`
- [ ] Local service running — see Scenario 1, Step 1
- [ ] VS Code extension built — run `npm install` inside `DevPilot.VSCodeExtension/`

**Quick health check:**

Open a browser or run `curl http://localhost:5071/health` to verify the service is responding.

---

## Scenario 1: Repository Indexing and Semantic Search

**Goal:** Index a codebase and query it using natural language.

### Setup

Start the local service (if not already running):

```powershell
dotnet run --project DevPilot/src/DevPilot.CLI/DevPilot.CLI.csproj service
```

The service starts on port `5071`. Wait for the startup confirmation message before proceeding.

### Steps

**1. Index a codebase**

Index DevPilot's own source code as a test target:

```powershell
dotnet run --project DevPilot/src/DevPilot.CLI/DevPilot.CLI.csproj index DevPilot/src
```

**2. Run a semantic query**

```powershell
dotnet run --project DevPilot/src/DevPilot.CLI/DevPilot.CLI.csproj ask "How does the SQLite connection factory configure WAL?"
```

### Expected Result

- The indexer scans source files, extracts code chunks, generates embeddings, and writes them to the local SQLite database.
- The `ask` command retrieves relevant code blocks via vector similarity, assembles a context-aware prompt, and streams a response from the local Phi-3 model.
- The response should reference specific files and code patterns from the indexed repository.

### Troubleshooting

- If the `ask` command returns no results, verify the index step completed without errors.
- If the service fails to start, check that port `5071` is not already in use.
- If model loading fails, rerun `.\scripts\validate-models.ps1`.

---

## Scenario 2: VS Code Sidebar Chat

**Goal:** Demonstrate the VS Code extension communicating with the local service.

### Setup

Ensure the local service is running (see Scenario 1).

### Steps

1. Open the `DevPilot.VSCodeExtension` folder in VS Code.
2. Press **`F5`** to launch the Extension Development Host.
3. In the new VS Code window, click the **DevPilot icon** in the left Activity Bar.
4. Type a query in the chat panel, for example: `"Explain the database schemas used in DevPilot"`.

### Expected Result

- The sidebar panel opens with a chat interface.
- The query is sent to the local Kestrel service on port `5071`.
- A streamed response appears in the chat panel, grounded in the indexed codebase.

### Troubleshooting

- If the sidebar shows a connection error, verify the local service is running.
- If no response appears, check the VS Code Output panel (`DevPilot`) for error logs.
- If the extension fails to activate, try reloading the Extension Host window (`Ctrl+Shift+P` → "Developer: Reload Window").

---

## Scenario 3: Code Patching and Validation

**Goal:** Show how DevPilot applies code edits and automatically rolls back if the build breaks.

### Setup

Ensure a codebase has been indexed (see Scenario 1) and the local service is running.

### Steps

**1. Trigger a modernization step via the REST API**

```powershell
Invoke-RestMethod `
  -Uri "http://localhost:5071/modernization/execute" `
  -Method Post `
  -ContentType "application/json" `
  -Body '{"PlanId": "YOUR_PLAN_ID", "Action": "execute-step", "StepId": "step-1"}'
```

Replace `YOUR_PLAN_ID` with an active plan ID from a prior modernization request.

**2. Observe the patch**

The service applies a search-replace edit to the target file.

**3. Introduce a deliberate error**

Manually edit the patched file to introduce a syntax error (e.g. delete a closing brace).

**4. Run validation**

The service detects the compilation failure, parses the error output, and rolls back the edit to restore a clean build state.

### Expected Result

- The patch is applied to the target file.
- After the deliberate error is introduced, the validation step detects the build failure.
- The file is automatically reverted to its pre-patch state.
- The build returns to a passing state.

### Troubleshooting

- If the modernization endpoint returns a 404, verify the plan ID exists.
- If rollback does not trigger, check that the service has write access to the target files.
