using System;
using System.Diagnostics;
using DevPilot.Contracts;

namespace DevPilot.Core.Productization;

public sealed class RuntimeDiagnosticsManager : IRuntimeDiagnosticsManager
{
    private readonly ISettingsManager _settingsManager;

    public RuntimeDiagnosticsManager(ISettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
    }

    public double GetTokenThroughput()
    {
        // Simulated real-time LLM token generation speed (tokens per second)
        var random = new Random();
        var settings = _settingsManager.GetSettings();
        
        // Slightly faster throughput on GPU/DirectML
        double baseSpeed = settings.HardwareProviderPreference.Equals("DirectML", StringComparison.OrdinalIgnoreCase) ? 22.5 : 12.0;
        return Math.Round(baseSpeed + random.NextDouble() * 3.5, 2);
    }

    public long GetPeakWorkingSetMemory()
    {
        // Read active process peak working memory footprint
        using var process = Process.GetCurrentProcess();
        return process.PeakWorkingSet64;
    }

    public string GetActiveDeviceDescription()
    {
        var settings = _settingsManager.GetSettings();
        return $"Execution Provider: {settings.HardwareProviderPreference} | Hardware Platform: {Environment.OSVersion.Platform} (x64)";
    }
}
