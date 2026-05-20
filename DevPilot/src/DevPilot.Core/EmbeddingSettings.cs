namespace DevPilot.Core;

public sealed class EmbeddingSettings
{
    public bool GenerateDuringIndexing { get; init; } = true;

    public string ModelName { get; init; } = "all-MiniLM-L6-v2";

    public string ModelPath { get; init; } = "models/embeddings/all-MiniLM-L6-v2/model.onnx";

    public string VocabularyPath { get; init; } = "models/embeddings/all-MiniLM-L6-v2/vocab.txt";

    public int Dimensions { get; init; } = 384;

    public int BatchSize { get; init; } = 16;

    public int MaxTokens { get; init; } = 256;

    public bool AllowDeterministicFallback { get; init; } = true;
}
