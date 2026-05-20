using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;

namespace DevPilot.AI;

public sealed class ExecutionProviderSelector : IExecutionProviderSelector
{
    private readonly RuntimeOptimizationSettings _settings;

    public ExecutionProviderSelector(IOptions<RuntimeOptimizationSettings> settings)
    {
        _settings = settings.Value;
    }

    public ExecutionProviderKind SelectProvider()
    {
        var providers = OrtEnv.Instance().GetAvailableProviders();

        if (providers.Contains("CUDAExecutionProvider"))
        {
            return ExecutionProviderKind.Cuda;
        }

        if (providers.Contains("DmlExecutionProvider"))
        {
            return ExecutionProviderKind.DirectML;
        }

        return ExecutionProviderKind.Cpu;
    }

    public IReadOnlyList<ExecutionProviderStatus> GetProviderStatuses()
    {
        var providers = OrtEnv.Instance().GetAvailableProviders();
        var cudaAvailable = providers.Contains("CUDAExecutionProvider");
        var directMLAvailable = providers.Contains("DmlExecutionProvider");

        var selected = SelectProvider();

        return
        [
            new ExecutionProviderStatus(
                ExecutionProviderKind.Cuda,
                cudaAvailable,
                selected == ExecutionProviderKind.Cuda,
                cudaAvailable
                    ? "CUDA execution provider is available."
                    : "CUDA execution provider is not available."),

            new ExecutionProviderStatus(
                ExecutionProviderKind.DirectML,
                directMLAvailable,
                selected == ExecutionProviderKind.DirectML,
                directMLAvailable
                    ? "DirectML execution provider is available."
                    : "DirectML execution provider is not available."),

            new ExecutionProviderStatus(
                ExecutionProviderKind.Cpu,
                true,
                selected == ExecutionProviderKind.Cpu,
                "CPU execution provider is always available."),

            new ExecutionProviderStatus(
                ExecutionProviderKind.WindowsML,
                false,
                false,
                "Windows ML provider is reserved for future optimization."),

            new ExecutionProviderStatus(
                ExecutionProviderKind.Npu,
                false,
                false,
                "NPU provider is reserved for future optimization.")
        ];
    }
}