using Microsoft.Extensions.DependencyInjection;
using DevPilot.Contracts;

namespace DevPilot.AI;

public static class AiRegistration
{
    public static IServiceCollection AddDevPilotAi(this IServiceCollection services)
    {
        services.AddSingleton<IExecutionProviderSelector, ExecutionProviderSelector>();
        services.AddSingleton<IRuntimeCapabilityService, RuntimeCapabilityService>();
        services.AddSingleton<OnnxSessionFactory>();
        services.AddSingleton<OnnxModelTokenizer>();
        services.AddSingleton<IEmbeddingTokenizer>(provider => provider.GetRequiredService<OnnxModelTokenizer>());
        services.AddSingleton<ILlmTokenizer>(provider => provider.GetRequiredService<OnnxModelTokenizer>());
        services.AddSingleton<ITokenizerValidationService, TokenizerValidationService>();
        services.AddSingleton<OnnxEmbeddingModel>();
        services.AddSingleton<IEmbeddingService, OnnxEmbeddingService>();
        services.AddSingleton<IEmbeddingPipelineService, EmbeddingPipelineService>();
        services.AddSingleton<ISemanticSearchService, SemanticSearchService>();
        services.AddSingleton<IRetrievalDiagnosticsService, RetrievalDiagnosticsService>();
        services.AddSingleton<ILLMService, OnnxLLMService>();
        services.AddSingleton<IInferenceProfiler, InferenceProfiler>();
        services.AddSingleton<IStreamingValidationService, StreamingValidationService>();
        services.AddSingleton<IModelValidationService, ModelValidationService>();
        services.AddSingleton<IModelManager, DevPilot.AI.Registry.ModelRegistry>();
        services.AddSingleton<DevPilot.AI.Diagnostics.ExecutionVerificationService>();

        return services;
    }
}
