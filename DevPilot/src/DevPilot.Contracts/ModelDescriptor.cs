namespace DevPilot.Contracts;

public sealed class ModelDescriptor
{
    public string Name { get; init; } = "";
    public string ModelPath { get; init; } = "";
    public string TokenizerPath { get; init; } = "";
    public ModelExecutionTarget Target { get; init; }
    public bool SupportsKvCache { get; init; }
    public bool SupportsStreaming { get; init; }
    public bool UsesFp16 { get; init; }
}
