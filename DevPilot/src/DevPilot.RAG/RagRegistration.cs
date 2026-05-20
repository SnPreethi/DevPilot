using Microsoft.Extensions.DependencyInjection;
using DevPilot.Contracts;

namespace DevPilot.RAG;

public static class RagRegistration
{
    public static IServiceCollection AddDevPilotRag(this IServiceCollection services)
    {
        services.AddSingleton<IPromptBuilder, RepositoryAwarePromptBuilder>();
        services.AddSingleton<IContextOrchestrator, ContextOrchestrator>();
        services.AddSingleton<IRagPipeline, SimpleRagPipeline>();
        services.AddSingleton<IPromptDiagnosticsService, PromptDiagnosticsService>();
        services.AddSingleton<ICompletionContextBuilder, CompletionContextBuilder>();
        services.AddSingleton<DiagnosticAwarePromptBuilder>();
        services.AddSingleton<ExecutionAwarePromptBuilder>();
        services.AddSingleton<MemoryAwarePromptBuilder>();

        return services;
    }
}
