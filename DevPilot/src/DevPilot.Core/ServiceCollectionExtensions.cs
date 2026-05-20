using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DevPilot.Contracts;
using DevPilot.Core.Modernization;
using DevPilot.Core.Productization;
using DevPilot.Core.Workflow;

namespace DevPilot.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDevPilotCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ApplicationSettings>(configuration.GetSection(DevPilotConstants.ApplicationSection));
        services.Configure<StorageSettings>(configuration.GetSection(DevPilotConstants.StorageSection));
        services.Configure<IndexingSettings>(configuration.GetSection(DevPilotConstants.IndexingSection));
        services.Configure<ModelSettings>(configuration.GetSection(DevPilotConstants.ModelsSection));
        services.Configure<EmbeddingSettings>(configuration.GetSection(DevPilotConstants.EmbeddingsSection));
        services.Configure<VectorSearchSettings>(configuration.GetSection(DevPilotConstants.VectorSearchSection));
        services.Configure<LLMSettings>(configuration.GetSection(DevPilotConstants.LLMSection));
        services.Configure<RagSettings>(configuration.GetSection(DevPilotConstants.RagSection));
        services.Configure<PromptingSettings>(configuration.GetSection(DevPilotConstants.PromptingSection));
        services.Configure<DiagnosticsSettings>(configuration.GetSection(DevPilotConstants.DiagnosticsSection));
        services.Configure<IncrementalIndexingSettings>(configuration.GetSection(DevPilotConstants.IncrementalIndexingSection));
        services.Configure<PerformanceSettings>(configuration.GetSection(DevPilotConstants.PerformanceSection));
        services.Configure<EmbeddingVersioningSettings>(configuration.GetSection(DevPilotConstants.EmbeddingVersioningSection));
        services.Configure<TokenEstimationSettings>(configuration.GetSection(DevPilotConstants.TokenEstimationSection));
        services.Configure<RuntimeOptimizationSettings>(configuration.GetSection(DevPilotConstants.RuntimeOptimizationSection));
        services.Configure<InferenceSettings>(configuration.GetSection(DevPilotConstants.InferenceSection));
        services.Configure<TokenizerSettings>(configuration.GetSection(DevPilotConstants.TokenizerSection));
        services.Configure<StreamingSettings>(configuration.GetSection(DevPilotConstants.StreamingSection));
        services.Configure<ExecutionProviderSettings>(configuration.GetSection(DevPilotConstants.ExecutionProvidersSection));
        services.AddSingleton<ITokenEstimator, ApproximateTokenEstimator>();
        services.AddSingleton<DevPilot.Core.Diagnostics.DiagnosticsOrchestrator>();
        services.AddSingleton<DevPilot.Core.Execution.TerminalOrchestrator>();
        services.AddSingleton<DevPilot.Core.Execution.ExecutionContextOrchestrator>();
        services.AddSingleton<DevPilot.Core.Execution.ExecutionPipelineOrchestrator>();
        services.AddSingleton<IExecutionPipelineOrchestrator>(sp => sp.GetRequiredService<DevPilot.Core.Execution.ExecutionPipelineOrchestrator>());
        services.AddSingleton<DevPilot.Core.Memory.ConventionAnalyzer>();
        services.AddSingleton<DevPilot.Core.Memory.ArchitectureAnalyzer>();
        services.AddSingleton<DevPilot.Core.Memory.PersistentContextOrchestrator>();
        services.AddSingleton<IEngineeringWorkflowPlanner, EngineeringWorkflowPlanner>();
        services.AddSingleton<ITaskGraphOrchestrator, TaskGraphOrchestrator>();
        services.AddSingleton<IGraphTraversalService, DevPilot.Core.Graph.GraphTraversalService>();

        // Contextual Reasoning Engine
        services.AddSingleton<IReasoningEvidenceChainBuilder, DevPilot.Core.Reasoning.ReasoningEvidenceChainBuilder>();
        services.AddSingleton<IEngineeringCorrelationEngine, DevPilot.Core.Reasoning.EngineeringCorrelationEngine>();
        services.AddSingleton<IRootCauseReasoner, DevPilot.Core.Reasoning.RootCauseReasoner>();
        services.AddSingleton<IContextRankingEngine, DevPilot.Core.Reasoning.ContextRankingEngine>();

        // Failure Attribution Engine
        services.AddSingleton<IFailureAttributionEngine, DevPilot.Core.Failure.FailureAttributionEngine>();
        services.AddSingleton<IPatchImpactAnalyzer, DevPilot.Core.Failure.PatchImpactAnalyzer>();
        services.AddSingleton<IFailureLineageResolver, DevPilot.Core.Failure.FailureLineageResolver>();

        // Architecture Reasoning Engine
        services.AddSingleton<IDependencyBoundaryAnalyzer, DevPilot.Core.Architecture.DependencyBoundaryAnalyzer>();
        services.AddSingleton<IConventionViolationAnalyzer, DevPilot.Core.Architecture.ConventionViolationAnalyzer>();
        services.AddSingleton<IMigrationImpactAnalyzer, DevPilot.Core.Architecture.MigrationImpactAnalyzer>();
        services.AddSingleton<IArchitectureReasoningEngine, DevPilot.Core.Architecture.ArchitectureReasoningEngine>();

        // Modernization Workflows Engine
        services.AddSingleton<IDependencyImpactAnalyzer, DevPilot.Core.Modernization.DependencyImpactAnalyzer>();
        services.AddSingleton<IModernizationPlanner, DevPilot.Core.Modernization.ModernizationPlanner>();
        services.AddSingleton<ModernizationEngine>();
        services.AddSingleton<IModernizationEngine>(sp => sp.GetRequiredService<ModernizationEngine>());

        // Productization Layer Services
        services.AddSingleton<ISettingsManager, SettingsManager>();
        services.AddSingleton<IProductModelManager, ProductModelManager>();
        services.AddSingleton<IDependencyBootstrapper, DependencyBootstrapper>();
        services.AddSingleton<IRuntimeDiagnosticsManager, RuntimeDiagnosticsManager>();
        services.AddSingleton<IOnboardingManager, OnboardingManager>();
        services.AddSingleton<IUpdateManager, UpdateManager>();
        services.AddSingleton<ILogViewerService, LogViewerService>();

        return services;
    }
}
