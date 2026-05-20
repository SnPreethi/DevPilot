using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevPilot.Contracts;
using DevPilot.Core.Productization;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DevPilot.Core.Tests;

public sealed class ProductizationLayerTests : IDisposable
{
    private readonly string _tempSettingsDir;
    private readonly string _tempSettingsFile;

    public ProductizationLayerTests()
    {
        _tempSettingsDir = Path.Combine(Path.GetTempPath(), "DevPilotTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempSettingsDir);
        _tempSettingsFile = Path.Combine(_tempSettingsDir, "devpilot-settings.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempSettingsDir))
            {
                Directory.Delete(_tempSettingsDir, true);
            }
        }
        catch
        {
            // Ignore clean up errors
        }
    }

    private class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {}
    }

    [Fact]
    public void SettingsManager_ShouldLoadDefaults_AndPersistChanges()
    {
        var manager = new SettingsManager(_tempSettingsFile);
        var initialSettings = manager.GetSettings();

        Assert.NotNull(initialSettings);
        Assert.Contains("DevPilot", initialSettings.ModelStoragePath);
        Assert.Equal("Phi-3-Mini-Instruct-ONNX", initialSettings.ActiveLlmModel);

        var updated = new ProductSettings(
            ModelStoragePath: "C:\\Custom\\Path",
            ActiveLlmModel: "Llama-3-8B-Instruct-ONNX",
            ActiveEmbeddingModel: "BGE-Small-v1.5-ONNX",
            HardwareProviderPreference: "DirectML",
            LogLevelThreshold: "Warning"
        );

        manager.SaveSettings(updated);

        var loaded = manager.GetSettings();
        Assert.Equal("C:\\Custom\\Path", loaded.ModelStoragePath);
        Assert.Equal("Llama-3-8B-Instruct-ONNX", loaded.ActiveLlmModel);
        Assert.Equal("Warning", loaded.LogLevelThreshold);
    }

    [Fact]
    public async Task ProductModelManager_ShouldTrackDownloadProgression_AndSimulateCorrectly()
    {
        var settingsManager = new SettingsManager(_tempSettingsFile);
        var manager = new ProductModelManager(settingsManager, new TestLogger<ProductModelManager>());

        var initialList = manager.GetModelsStatus().ToList();
        Assert.Equal(2, initialList.Count);
        
        var phiModel = initialList.First(m => m.ModelId == "Phi-3-Mini-Instruct-ONNX");
        Assert.Equal(ModelStatus.Missing, phiModel.Status);

        // Start mock background download
        var cts = new CancellationTokenSource();
        var downloadTask = manager.StartDownloadAsync("Phi-3-Mini-Instruct-ONNX", cts.Token);

        // Wait up to 1 second for background downloader to kick off
        ModelDownloadProgress phiDuring = null;
        for (int i = 0; i < 20; i++)
        {
            var currentStatus = manager.GetModelsStatus().First(m => m.ModelId == "Phi-3-Mini-Instruct-ONNX");
            if (currentStatus.Status == ModelStatus.Downloading || currentStatus.Status == ModelStatus.Ready)
            {
                phiDuring = currentStatus;
                break;
            }
            await Task.Delay(50);
        }

        Assert.NotNull(phiDuring);
        Assert.True(phiDuring.Status == ModelStatus.Downloading || phiDuring.Status == ModelStatus.Ready);

        // Cancel the progress
        await manager.CancelDownloadAsync("Phi-3-Mini-Instruct-ONNX");
    }

    [Fact]
    public void DependencyBootstrapper_ShouldAuditSystemDLLs_AndAllowRepair()
    {
        var settingsManager = new SettingsManager(_tempSettingsFile);
        var bootstrapper = new DependencyBootstrapper(settingsManager, new TestLogger<DependencyBootstrapper>());

        var result = bootstrapper.VerifyDependencies();
        Assert.NotEmpty(result);
        Assert.Contains(result, d => d.Name.Contains("Visual C++"));
    }

    [Fact]
    public void RuntimeDiagnostics_ShouldProvideValidTelemetry()
    {
        var settingsManager = new SettingsManager(_tempSettingsFile);
        var diagnostics = new RuntimeDiagnosticsManager(settingsManager);

        Assert.True(diagnostics.GetTokenThroughput() >= 0);
        Assert.True(diagnostics.GetPeakWorkingSetMemory() > 0);
        Assert.Contains("Execution Provider", diagnostics.GetActiveDeviceDescription());
    }

    [Fact]
    public void OnboardingManager_ShouldHandleSetupWizardCompletion()
    {
        var settingsManager = new SettingsManager(_tempSettingsFile);
        var tempOnboardingFile = Path.Combine(_tempSettingsDir, "devpilot-onboarding.json");
        var manager = new OnboardingManager(settingsManager, tempOnboardingFile);

        Assert.False(manager.IsOnboardingCompleted());
        Assert.NotEmpty(manager.DetectHardwareCapabilities());

        manager.CompleteOnboarding();
        Assert.True(manager.IsOnboardingCompleted());
    }

    [Fact]
    public async Task UpdateManager_ShouldReturnUpdates_AndApplyOTA()
    {
        var manager = new UpdateManager(new TestLogger<UpdateManager>());
        var status = manager.CheckForUpdates();

        Assert.True(status.IsAvailable);
        Assert.Equal("1.1.0", status.TargetVersion);

        var success = await manager.ApplyUpdateAsync(CancellationToken.None);
        Assert.True(success);
    }

    [Fact]
    public void LogViewerService_ShouldRecordLogs()
    {
        var service = new LogViewerService();
        var logs = service.RetrieveLatestLogs(10);
        Assert.NotEmpty(logs);
        Assert.Equal(10, logs.Count());
    }
}
