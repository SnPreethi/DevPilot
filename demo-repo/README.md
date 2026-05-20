# DevPilot Enterprise Showcase Demo Repository

Welcome to the DevPilot Showcase Demo Repository. This workspace is intentionally structured with standard legacy enterprise technical debt, outdated synchronicity patterns, failing test workflows, and hardcoded secrets.

It serves as the **perfect live showcase sandbox** to demonstrate DevPilot's offline-first reasoning, semantic codebase scanning, and rollback-safe automated modernizations.

---

## Sandbox Technical Debt Summary

1. **Outdated Synchronous HTTP Requests (`services/PaymentService.cs`):**
   * Uses legacy `HttpWebRequest` instead of async `HttpClientFactory`.
   * **The Security Risk:** Hardcoded development API credentials (`sk_prod_secret_12345`) embedded directly in source logic!
2. **Crash Risks / Division by Zero (`services/PaymentService.cs`):**
   * `CalculateFee` divides by zero if the tier parameter is unrecognized (e.g. `"Free"`).
3. **Failing NUnit Test Suite (`tests/PaymentServiceTests.cs`):**
   * The test case `CalculateFee_FreeTier_ShouldNotCrash` throws a division by zero crash.
4. **SQLite Concurrency Locks (`services/DatabaseLogger.cs`):**
   * Lacks write-ahead logging (WAL) connection configurations, locking up thread loops during heavy logging events.

---

## Live Showcase Walkthrough

### Walkthrough 1: Semantic Codebase Indexing & Search
Demonstrate semantic code lookup on the demo repository:
1. Index the demo directory:
   ```powershell
   dotnet run --project DevPilot/src/DevPilot.CLI/DevPilot.CLI.csproj index demo-repo
   ```
2. Query security issues:
   ```powershell
   dotnet run --project DevPilot/src/DevPilot.CLI/DevPilot.CLI.csproj ask "Are there any hardcoded secret keys or credentials in the services?"
   ```
3. **Observe:** DevPilot immediately locates `PaymentService.cs`, highlights the line containing the bearer key, and recommends extracting it to settings!

### Walkthrough 2: AI Code Modernization & Rollbacks
Demonstrate automated plan orchestration:
1. Start an active plan to upgrade `PaymentService` to async HttpClient:
   * Select `ProcessTransaction` in VS Code, right-click, and select "DevPilot: Refactor to Async HttpClient".
2. **Observe:** The `SearchReplacePatchEngine` generates the correct async pattern modifications, opening a visual side-by-side diff.
3. Apply the changes.
4. Trigger an intentional validation crash by running NUnit tests.
5. **Observe:** DevPilot captures the division by zero error, attributes it to `CalculateFee`, and immediately rolls back the workspace to safety!
