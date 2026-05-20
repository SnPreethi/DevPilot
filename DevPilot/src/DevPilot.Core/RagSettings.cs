namespace DevPilot.Core;

public sealed class RagSettings
{
    public int RetrievalCount { get; init; } = 5;

    public int MaxContextChunks { get; init; } = 5;
}
