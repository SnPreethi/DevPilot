using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using System.Diagnostics;

namespace DevPilot.AI.Diagnostics;

public sealed class ExecutionVerificationService
{
    private readonly OnnxSessionFactory _sessionFactory;
    private readonly ILLMService _llmService;
    private readonly IExecutionProviderSelector _providerSelector;
    private readonly IModelManager _modelManager;
    private readonly ILogger<ExecutionVerificationService> _logger;

    public ExecutionVerificationService(
        OnnxSessionFactory sessionFactory,
        ILLMService llmService,
        IExecutionProviderSelector providerSelector,
        IModelManager modelManager,
        ILogger<ExecutionVerificationService> logger)
    {
        _sessionFactory = sessionFactory;
        _llmService = llmService;
        _providerSelector = providerSelector;
        _modelManager = modelManager;
        _logger = logger;
    }

    public Task VerifyExecutionAsync(CancellationToken cancellationToken = default)
    {
        var providers = OrtEnv.Instance().GetAvailableProviders();
        var cudaAvailable = providers.Contains("CUDAExecutionProvider");
        var dmlAvailable = providers.Contains("DmlExecutionProvider");

        var activeProvider = _sessionFactory.ActiveProvider;
        var modelDescriptor = _modelManager.Resolve(activeProvider);

        _logger.LogInformation("=== Execution Verification Diagnostics ===");
        _logger.LogInformation("CUDA Available: {CudaAvailable}", cudaAvailable);
        _logger.LogInformation("DirectML Available: {DmlAvailable}", dmlAvailable);
        _logger.LogInformation("Actual Active Execution Provider: {ActiveProvider}", activeProvider);
        _logger.LogInformation("Actual Model Target: {ModelTarget}", modelDescriptor.Target);
        _logger.LogInformation("Model Path: {ModelPath}", modelDescriptor.ModelPath);
        _logger.LogInformation("KV Cache Supported: {SupportsKvCache}", modelDescriptor.SupportsKvCache);
        
        var fallbackObserved = activeProvider != ExecutionProviderKind.Cuda && cudaAvailable;
        _logger.LogInformation("Provider Fallback Observed: {FallbackObserved}", fallbackObserved);

        if (_llmService is OnnxLLMService onnxService)
        {
            _logger.LogInformation("Session Provider Loaded: {IsLoaded}", onnxService.IsLoaded);
            _logger.LogInformation("Inference Initial Load Duration: {LoadDuration}", onnxService.LoadDuration);
        }

        // VRAM, Graph Partitioning Warnings and detailed metrics require ORT Telemetry/NVIDIA SMI
        // which are beyond simple C# inspection, but the configuration ensures minimal mixed graphs
        // due to using provider-specific model variants.

        _logger.LogInformation("==========================================");

        return Task.CompletedTask;
    }
}
