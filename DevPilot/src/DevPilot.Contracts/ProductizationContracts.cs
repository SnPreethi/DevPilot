using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevPilot.Contracts;

public enum ModelStatus
{
    Missing,
    Downloading,
    Ready,
    Failed
}

public enum DependencyHealth
{
    Healthy,
    Missing,
    Corrupted
}

public record ProductSettings(
    string ModelStoragePath,
    string ActiveLlmModel,
    string ActiveEmbeddingModel,
    string HardwareProviderPreference,
    string LogLevelThreshold
);

public record ModelDownloadProgress(
    string ModelId,
    ModelStatus Status,
    long BytesReceived,
    long TotalBytes,
    double Percentage,
    string DownloadSpeedEstimate
);

public record DependencyItem(
    string Name,
    DependencyHealth Health,
    bool IsCritical,
    string Description
);

public record AppUpdateInfo(
    string CurrentVersion,
    string TargetVersion,
    bool IsAvailable,
    bool IsMandatory,
    string ReleaseNotes
);

public record LogLine(
    DateTime Timestamp,
    string Level,
    string Source,
    string Message,
    string StackTrace
);

public interface ISettingsManager
{
    ProductSettings GetSettings();
    void SaveSettings(ProductSettings settings);
}

public interface IProductModelManager
{
    IEnumerable<ModelDownloadProgress> GetModelsStatus();
    Task StartDownloadAsync(string modelId, CancellationToken cancellationToken = default);
    Task CancelDownloadAsync(string modelId);
}

public interface IDependencyBootstrapper
{
    IEnumerable<DependencyItem> VerifyDependencies();
    Task<bool> RunRepairToolAsync(string dependencyName);
}

public interface IRuntimeDiagnosticsManager
{
    double GetTokenThroughput();
    long GetPeakWorkingSetMemory();
    string GetActiveDeviceDescription();
}

public interface IOnboardingManager
{
    bool IsOnboardingCompleted();
    void CompleteOnboarding();
    string DetectHardwareCapabilities();
}

public interface IUpdateManager
{
    AppUpdateInfo CheckForUpdates();
    Task<bool> ApplyUpdateAsync(CancellationToken cancellationToken = default);
}

public interface ILogViewerService
{
    IEnumerable<LogLine> RetrieveLatestLogs(int rowCount);
}
