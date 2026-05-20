using DevPilot.Contracts;

namespace DevPilot.Core;

public sealed class RuntimeOptimizationSettings
{
    public string[] PreferredExecutionProviders { get; init; } = ["Cuda", "DirectML", "CPU"];
    
    public bool AllowProviderFallback { get; init; } = true;

    public bool EnableMemoryPattern { get; init; } = true;

    public bool EnableCpuMemoryArena { get; init; } = true;
}
