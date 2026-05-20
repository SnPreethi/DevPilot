namespace DevPilot.Core;

public sealed class StreamingSettings
{
    public bool Enabled { get; init; } = true;

    public int CancellationProbeTokens { get; init; } = 2;
}
