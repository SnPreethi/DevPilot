namespace DevPilot.UI.Models;

public sealed record RuntimeSettingsView(
    string EmbeddingModelPath,
    string LlmModelPath,
    int DefaultTopK,
    int MaxPromptCharacters,
    int MaxChunkCharacters,
    bool OfflineOnly,
    string RuntimeStatus,
    string HardwareSummary);
