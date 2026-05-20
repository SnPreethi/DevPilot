namespace DevPilot.Core;

public sealed class ExecutionProviderSettings
{
    public string PreferredProvider { get; init; } = "DirectML";

    public string FallbackProvider { get; init; } = "CPU";

    public bool AllowFallback { get; init; } = true;
}
