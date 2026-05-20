using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Options;

namespace DevPilot.AI;

/// <summary>
/// Retained for backward compatibility. Model resolution is now handled by ModelRegistry.
/// This class provides simple listing of configured model paths for diagnostics.
/// </summary>
public sealed class LocalModelManager
{
    private readonly EmbeddingSettings _embeddingSettings;
    private readonly LLMSettings _llmSettings;

    public LocalModelManager(
        IOptions<EmbeddingSettings> embeddingSettings,
        IOptions<LLMSettings> llmSettings)
    {
        _embeddingSettings = embeddingSettings.Value;
        _llmSettings = llmSettings.Value;
    }

    public IReadOnlyList<ModelDescriptor> ListModels()
    {
        return
        [
            new ModelDescriptor
            {
                Name = _embeddingSettings.ModelName,
                ModelPath = Path.GetFullPath(_embeddingSettings.ModelPath),
                Target = ModelExecutionTarget.Cpu,
                SupportsKvCache = false,
                SupportsStreaming = false
            },
            new ModelDescriptor
            {
                Name = _llmSettings.ModelId,
                ModelPath = Path.GetFullPath(_llmSettings.ModelPath),
                Target = ModelExecutionTarget.Cpu,
                SupportsKvCache = true,
                SupportsStreaming = true
            }
        ];
    }
}
