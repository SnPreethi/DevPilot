# DevPilot — Workflow Execution

How DevPilot coordinates multi-step code operations, validates patches, and recovers from failures.

---

## Workflow Execution

DevPilot supports multi-step code modification workflows such as refactoring deprecated APIs or upgrading framework versions. Workflows are developer-supervised — every plan requires review before execution, and each step is validated before proceeding.

```text
Create Plan → Developer Review → Execute Step → Validate → Next Step (or Rollback)
```

### Execution flow

1. **Plan generation**: The system scans the codebase to identify files that require changes and produces a structured plan with discrete, ordered steps.
2. **Review and approval**: The developer reviews the proposed plan before any code is modified. Steps can be approved individually through the VS Code sidebar or via the REST API.
3. **Execution**: The patch engine applies search-replace edits to the target source files.
4. **Validation**: After each step, the local compiler is invoked to check for syntax errors or regressions. If tests are available, they are run as well.
5. **Continuation or rollback**: If validation passes, the workflow advances to the next step. If it fails, the step is rolled back (see [Validation and Recovery](#validation-and-recovery)).

---

## Patch Safety

Rather than performing raw file overwrites, the patch engine uses targeted search-replace operations. This approach reduces the risk of unintended changes to unrelated code.

**Key behaviors:**

* **Pattern matching**: Patches target specific text blocks identified by line coordinates and content. Only the matched region is modified.
* **Formatting preservation**: Code outside the patch target (comments, whitespace, styling) is left unchanged.
* **Conflict detection**: If a file has been modified by the developer since the plan was generated, the engine aborts the patch and reports the conflict rather than overwriting the developer's changes.

---

## Validation and Recovery

After each patch step, DevPilot runs the local compiler (and optionally the test suite) to verify the change did not introduce errors.

**If validation fails:**

* **Error attribution**: The compiler output is parsed to identify the file and line where the error was introduced.
* **Rollback**: The patched file is restored to its pre-edit state using a snapshot taken before the patch was applied. The workflow halts at the failed step for developer review.

Rollback is based on file snapshots retained during execution. It attempts to restore the codebase to the last known valid state, but developers should verify the result independently.

---

## Workflow Persistence

Workflow state is persisted locally so that interrupted sessions can be resumed.

* **Plan storage**: Active plans and step graphs are saved to the local SQLite database.
* **Checkpoint tracking**: Each completed step is recorded. If the service restarts, the workflow resumes from the last successful checkpoint.
* **Rollback snapshots**: Pre-patch file snapshots are retained on disk until the workflow completes or is explicitly discarded.

---

## Current Limitations

* **Experimental**: The workflow engine is under active development. Edge cases in multi-file patching may not be fully handled.
* **Heuristic patching**: Search-replace edits are text-based, not AST-based. Complex refactorings that require structural code transformations may produce incorrect patches.
* **Validation depends on local tooling**: Validation quality is limited by the local compiler and test coverage. If there are no tests for the modified code, regressions may not be caught.
* **Developer supervision required**: Workflows do not execute autonomously. Plan approval, step review, and post-rollback verification remain the developer's responsibility.
