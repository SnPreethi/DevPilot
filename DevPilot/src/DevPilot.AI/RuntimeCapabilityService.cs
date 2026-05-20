using DevPilot.Contracts;
using System.Runtime.InteropServices;

namespace DevPilot.AI;

public sealed class RuntimeCapabilityService : IRuntimeCapabilityService
{
    private readonly IExecutionProviderSelector _providerSelector;
    private readonly OnnxSessionFactory _sessionFactory;

    public RuntimeCapabilityService(IExecutionProviderSelector providerSelector, OnnxSessionFactory sessionFactory)
    {
        _providerSelector = providerSelector;
        _sessionFactory = sessionFactory;
    }

    public RuntimeCapabilityReport GetCapabilities()
    {
        var providers = _providerSelector.GetProviderStatuses();
        var selected = _sessionFactory.ActiveProvider;
        var hardware = new RuntimeHardwareInfo(
            RuntimeInformation.ProcessArchitecture.ToString(),
            GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            Environment.WorkingSet,
            GpuDetected: providers.Any(provider => (provider.Provider == ExecutionProviderKind.Cuda || provider.Provider == ExecutionProviderKind.DirectML) && provider.IsAvailable),
            NpuDetected: false,
            DirectMLAvailable: providers.Any(provider => provider.Provider == ExecutionProviderKind.DirectML && provider.IsAvailable),
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString());

        return new RuntimeCapabilityReport(
            hardware,
            providers,
            selected,
            selected != ExecutionProviderKind.Cpu,
            DateTimeOffset.UtcNow);
    }
}
