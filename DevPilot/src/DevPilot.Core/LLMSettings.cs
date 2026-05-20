namespace DevPilot.Core;

public sealed class LLMSettings
{
    public string ModelId { get; init; } = "phi-3-mini-4k-instruct";

    public string ModelPath { get; init; } = "models/llm/phi-3-mini/model.onnx";

    public string TokenizerModelPath { get; init; } = "models/llm/phi-3-mini/tokenizer.model";

    public int MaxSequenceLength { get; init; } = 4096;

    public int MaxOutputTokens { get; init; } = 32;

    public double Temperature { get; init; } = 0.2;

    public double TopP { get; init; } = 0.9;

    public int[] StopTokenIds { get; init; } = [2, 32000, 32007];

    public bool AllowExtractiveFallback { get; init; } = true;

    public bool EnableTokenLogging { get; init; } = true;

    public bool EnableTensorLogging { get; init; } = true;

    public bool EnableStreamingLogging { get; init; } = true;

    public string DiagnosticsLevel { get; set; } = "Production";
}
