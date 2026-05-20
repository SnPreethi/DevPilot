namespace DevPilot.Contracts;

public sealed record EmbeddingResult(
    string ModelId,
    IReadOnlyList<float> Vector,
    int Dimension);
