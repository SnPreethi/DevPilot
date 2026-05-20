# DevPilot Architecture

The current architecture is local-first and intentionally simple:

```text
WinUI / CLI
  -> Application Services
  -> Existing Backend Contracts
  -> Indexing / Retrieval / Prompting / Inference
  -> SQLite and Local ONNX Models
```

`DevPilot.UI` adds the first Windows-native shell. It uses WinUI 3, Windows App SDK, and CommunityToolkit.Mvvm. The UI layer owns navigation, presentation state, commands, and user-friendly error messages. It does not own AI logic.

Desktop integration flow:

```text
Views
  -> ViewModels
  -> UI facade services
  -> IRepositoryIndexingService / ISemanticSearchService / IRagPipeline / diagnostics contracts
  -> Storage and local models
```

The desktop pages are:

- Repositories
- Search
- Assistant
- Diagnostics
- Settings

The backend remains reusable by both CLI and UI.
