using System;
using System.IO;
using System.Text.Json;
using DevPilot.Contracts;

namespace DevPilot.Core.Productization;

public sealed class OnboardingManager : IOnboardingManager
{
    private readonly ISettingsManager _settingsManager;
    private readonly string _onboardingStatePath;
    private readonly object _lock = new();

    public OnboardingManager(ISettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _onboardingStatePath = Path.Combine(appData, "DevPilot", "devpilot-onboarding.json");
    }

    // Secondary constructor allowing custom file path for testing
    public OnboardingManager(ISettingsManager settingsManager, string onboardingStatePath)
    {
        _settingsManager = settingsManager;
        _onboardingStatePath = onboardingStatePath;
    }

    public bool IsOnboardingCompleted()
    {
        lock (_lock)
        {
            if (!File.Exists(_onboardingStatePath))
            {
                return false;
            }

            try
            {
                var json = File.ReadAllText(_onboardingStatePath);
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.GetProperty("completed").GetBoolean();
            }
            catch
            {
                return false;
            }
        }
    }

    public void CompleteOnboarding()
    {
        lock (_lock)
        {
            var dir = Path.GetDirectoryName(_onboardingStatePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var data = new { completed = true, timestamp = DateTime.UtcNow };
            var json = JsonSerializer.Serialize(data);
            File.WriteAllText(_onboardingStatePath, json);
        }
    }

    public string DetectHardwareCapabilities()
    {
        var ramGb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024 / 1024;
        bool hasDirectML = File.Exists(Path.Combine(Environment.SystemDirectory, "d3d12.dll"));

        return $"Processor: {Environment.ProcessorCount} Cores | RAM Available: {ramGb} GB | Hardware GPU Accelerator: {(hasDirectML ? "DirectML Capable" : "Standard CPU Engine")}";
    }
}
