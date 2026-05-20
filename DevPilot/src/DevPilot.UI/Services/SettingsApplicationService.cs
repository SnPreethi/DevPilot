using DevPilot.Core;
using DevPilot.UI.Models;
using Microsoft.Extensions.Options;

namespace DevPilot.UI.Services;

public sealed class SettingsApplicationService : ISettingsApplicationService
{
    private readonly EmbeddingSettings _embeddingSettings;
    private readonly LLMSettings _llmSettings;
    private readonly VectorSearchSettings _vectorSearchSettings;
    private readonly PromptingSettings _promptingSettings;

    public SettingsApplicationService(
        IOptions<EmbeddingSettings> embeddingSettings,
        IOptions<LLMSettings> llmSettings,
        IOptions<VectorSearchSettings> vectorSearchSettings,
        IOptions<PromptingSettings> promptingSettings)
    {
        _embeddingSettings = embeddingSettings.Value;
        _llmSettings = llmSettings.Value;
        _vectorSearchSettings = vectorSearchSettings.Value;
        _promptingSettings = promptingSettings.Value;
    }

    public RuntimeSettingsView GetSettings()
    {
        return new RuntimeSettingsView(
            _embeddingSettings.ModelPath,
            _llmSettings.ModelPath,
            _vectorSearchSettings.DefaultTopK,
            _promptingSettings.MaxPromptCharacters,
            _promptingSettings.MaxChunkCharacters,
            true,
            "Local runtime configured",
            $"{Environment.MachineName} / {Environment.ProcessorCount} logical processors");
    }
}
