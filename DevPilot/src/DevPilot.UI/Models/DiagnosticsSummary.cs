namespace DevPilot.UI.Models;

public sealed record DiagnosticsSummary(
    IReadOnlyList<SearchResultItem> RetrievalMatches,
    string PromptPreview,
    int EstimatedPromptTokens,
    TimeSpan RetrievalDuration,
    IReadOnlyList<ChunkSummaryItem> Chunks,
    RuntimeDiagnosticsView Runtime);

public sealed record ChunkSummaryItem(
    string ChunkId,
    string FilePath,
    string? SymbolName,
    int StartLine,
    int EndLine,
    int TokenEstimate,
    string ChunkHash);

public sealed record RuntimeDiagnosticsView(
    string SelectedProvider,
    string ProviderStatus,
    string HardwareSummary,
    string EmbeddingModelStatus,
    string LlmModelStatus,
    string TokenizerStatus,
    string MemoryStatus,
    string InferenceTimingStatus);
