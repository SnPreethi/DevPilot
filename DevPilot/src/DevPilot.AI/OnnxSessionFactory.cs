using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;

namespace DevPilot.AI;

public sealed class OnnxSessionFactory
{
    private readonly IExecutionProviderSelector _providerSelector;
    private readonly RuntimeOptimizationSettings _settings;
    private readonly ILogger<OnnxSessionFactory> _logger;
    private ExecutionProviderKind _activeProvider = ExecutionProviderKind.Cpu;
    public ExecutionProviderKind ActiveProvider => _activeProvider;

    public OnnxSessionFactory(
        IExecutionProviderSelector providerSelector,
        IOptions<RuntimeOptimizationSettings> settings,
        ILogger<OnnxSessionFactory> logger)
    {
        _providerSelector = providerSelector;
        _settings = settings.Value;
        _logger = logger;
        _activeProvider = _providerSelector.SelectProvider();
    }

    public InferenceSession Create(string modelPath)
    {
        using var options = CreateOptions();
        return new InferenceSession(modelPath, options);
    }

    public SessionOptions CreateOptions()
    {
        var options = new SessionOptions
        {
            EnableMemoryPattern = _settings.EnableMemoryPattern,
            EnableCpuMemArena = _settings.EnableCpuMemoryArena,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_PARALLEL
        };

        // Thread pool optimizations: prevent over-subscription of CPU threads
        var targetThreads = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2));
        options.IntraOpNumThreads = targetThreads;
        options.InterOpNumThreads = targetThreads;

        var provider = _providerSelector.SelectProvider();

        if (provider == ExecutionProviderKind.Cuda)
        {
            try
            {
                using var cudaOptions = new OrtCUDAProviderOptions();
                cudaOptions.UpdateOptions(new Dictionary<string, string>
                {
                    { "device_id", "0" }
                });
                options.AppendExecutionProvider_CUDA(cudaOptions);
                _activeProvider = ExecutionProviderKind.Cuda;
                _logger.LogInformation("CUDA execution provider successfully attached.");
                return options;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CUDA provider could not be enabled. Falling back to DirectML.");
                provider = ExecutionProviderKind.DirectML; // fallback
            }
        }

        if (provider == ExecutionProviderKind.DirectML)
        {
            try
            {
                options.AppendExecutionProvider_DML(0);
                _activeProvider = ExecutionProviderKind.DirectML;
                _logger.LogInformation("DirectML execution provider successfully attached.");
                return options;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DirectML provider could not be enabled. Falling back to CPU.");
                provider = ExecutionProviderKind.Cpu; // fallback
            }
        }

        _activeProvider = ExecutionProviderKind.Cpu;
        _logger.LogInformation("ONNX Runtime configured for CPU execution provider.");
        return options;
    }
}
