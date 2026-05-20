using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DevPilot.Contracts;
using Microsoft.Extensions.Logging;

namespace DevPilot.Core.Productization;

public sealed class DependencyBootstrapper : IDependencyBootstrapper
{
    private readonly ISettingsManager _settingsManager;
    private readonly ILogger<DependencyBootstrapper> _logger;

    public DependencyBootstrapper(ISettingsManager settingsManager, ILogger<DependencyBootstrapper> logger)
    {
        _settingsManager = settingsManager;
        _logger = logger;
    }

    public IEnumerable<DependencyItem> VerifyDependencies()
    {
        var items = new List<DependencyItem>();

        // 1. C++ Runtime Redistributable Check
        bool cppHealthy = File.Exists(Path.Combine(Environment.SystemDirectory, "vcruntime140.dll"));
        items.Add(new DependencyItem(
            Name: "Microsoft Visual C++ Redistributable (x64)",
            Health: cppHealthy ? DependencyHealth.Healthy : DependencyHealth.Missing,
            IsCritical: true,
            Description: "Provides essential runtime libraries needed by the ONNX execution engine."
        ));

        // 2. ONNX Native Library Checks
        var localBin = AppContext.BaseDirectory;
        bool onnxNativeExists = File.Exists(Path.Combine(localBin, "onnxruntime.dll")) || 
                               File.Exists(Path.Combine(localBin, "onnxruntime.lib"));
        items.Add(new DependencyItem(
            Name: "ONNX Runtime Native Libraries",
            Health: onnxNativeExists ? DependencyHealth.Healthy : DependencyHealth.Corrupted,
            IsCritical: true,
            Description: "Native binary modules required for deep learning local inference acceleration."
        ));

        // 3. DirectML Direct3D API Layer
        bool dmlHealthy = File.Exists(Path.Combine(Environment.SystemDirectory, "d3d12.dll"));
        items.Add(new DependencyItem(
            Name: "DirectML Acceleration Compatibility",
            Health: dmlHealthy ? DependencyHealth.Healthy : DependencyHealth.Missing,
            IsCritical: false,
            Description: "Enables Windows DirectML hardware GPU acceleration."
        ));

        // 4. Settings Storage Path Validation
        var settings = _settingsManager.GetSettings();
        bool dirExists = Directory.Exists(settings.ModelStoragePath);
        items.Add(new DependencyItem(
            Name: "Model Storage Workspace Directory",
            Health: dirExists ? DependencyHealth.Healthy : DependencyHealth.Missing,
            IsCritical: true,
            Description: $"Workspace directory designated for model cache downloads: {settings.ModelStoragePath}"
        ));

        return items;
    }

    public Task<bool> RunRepairToolAsync(string dependencyName)
    {
        _logger.LogWarning("Triggering repair sequence for dependency: {DependencyName}.", dependencyName);

        // Simulated repair action
        if (dependencyName.Contains("Storage"))
        {
            var settings = _settingsManager.GetSettings();
            if (!Directory.Exists(settings.ModelStoragePath))
            {
                Directory.CreateDirectory(settings.ModelStoragePath);
            }
            return Task.FromResult(true);
        }

        // Return true to denote successful mock repair
        return Task.FromResult(true);
    }
}
