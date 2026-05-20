namespace DevPilot.Contracts;

public enum ExecutionProviderKind
{
    Cpu,
    Cuda,
    DirectML,
    WindowsML,
    Npu
}

public enum RuntimeValidationSeverity
{
    Info,
    Warning,
    Error
}

public sealed record ExecutionProviderStatus(
    ExecutionProviderKind Provider,
    bool IsAvailable,
    bool IsSelected,
    string Detail);

public sealed record RuntimeHardwareInfo(
    string CpuDescription,
    long TotalMemoryBytes,
    long CurrentProcessMemoryBytes,
    bool GpuDetected,
    bool NpuDetected,
    bool DirectMLAvailable,
    string OperatingSystem,
    string Architecture);

public sealed record RuntimeCapabilityReport(
    RuntimeHardwareInfo Hardware,
    IReadOnlyList<ExecutionProviderStatus> Providers,
    ExecutionProviderKind SelectedProvider,
    bool HardwareAccelerationEnabled,
    DateTimeOffset CapturedAtUtc);

public sealed record ModelValidationIssue(
    RuntimeValidationSeverity Severity,
    string Code,
    string Message);

public sealed record ModelValidationResult(
    string ModelName,
    string ModelPath,
    bool Exists,
    bool Loaded,
    bool IsCompatible,
    string ExecutionProvider,
    TimeSpan LoadDuration,
    IReadOnlyList<string> InputNames,
    IReadOnlyList<string> OutputNames,
    IReadOnlyList<ModelValidationIssue> Issues);

public sealed record RuntimeValidationReport(
    RuntimeCapabilityReport Runtime,
    ModelValidationResult EmbeddingModel,
    ModelValidationResult LlmModel,
    TokenizerValidationResult Tokenizer,
    InferenceProfile Profile);

public sealed record TokenizerValidationResult(
    bool IsCompatible,
    int RequestedMaxTokens,
    int ProducedTokens,
    int ActiveTokens,
    bool WasTruncated,
    int EstimatedPromptTokens,
    IReadOnlyList<ModelValidationIssue> Issues);

public sealed record InferenceProfile(
    TimeSpan EmbeddingDuration,
    TimeSpan RetrievalDuration,
    TimeSpan PromptBuildDuration,
    TimeSpan InferenceDuration,
    TimeSpan ModelValidationDuration,
    long MemoryBeforeBytes,
    long MemoryAfterBytes,
    bool UsedEmbeddingFallback,
    bool UsedLlmFallback,
    int StreamingTokens,
    string StreamingPartial);

public sealed record StreamingValidationResult(
    bool ProducedTokens,
    bool CancellationObserved,
    int TokensReceived,
    string PartialText,
    TimeSpan Duration,
    IReadOnlyList<ModelValidationIssue> Issues);

public interface IRuntimeCapabilityService
{
    RuntimeCapabilityReport GetCapabilities();
}

public interface IExecutionProviderSelector
{
    ExecutionProviderKind SelectProvider();

    IReadOnlyList<ExecutionProviderStatus> GetProviderStatuses();
}

public interface IModelValidationService
{
    Task<RuntimeValidationReport> ValidateAsync(
        string probePrompt,
        CancellationToken cancellationToken = default);
}

public interface ITokenizerValidationService
{
    TokenizerValidationResult Validate(string text, int maxTokens);
}

public interface IInferenceProfiler
{
    Task<InferenceProfile> ProfileAsync(
        string prompt,
        CancellationToken cancellationToken = default);
}

public interface IStreamingValidationService
{
    Task<StreamingValidationResult> ValidateAsync(
        string prompt,
        CancellationToken cancellationToken = default);
}
