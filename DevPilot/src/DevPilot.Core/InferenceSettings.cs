namespace DevPilot.Core;
public sealed class InferenceSettings
{
    public int MaxSequenceLength { get; init; } = 4096;
    public int MaxGenerationTokens { get; init; } = 512;
    public bool GreedyDecoding { get; init; } = true;
    // =========================
    // Embedding Model
    // =========================
    public string EmbeddingModelPath { get; init; } =
        "models/embeddings/all-MiniLM-L6-v2/model.onnx";
    // =========================
    // Phi-3 Model
    // =========================
    public string PhiModelPath { get; init; } =
        "models/llm/phi-3-mini/model.onnx";
    // =========================
    // Tokenizer
    // =========================
    public string TokenizerPath { get; init; } =
        "models/llm/phi-3-mini/tokenizer.model";
}