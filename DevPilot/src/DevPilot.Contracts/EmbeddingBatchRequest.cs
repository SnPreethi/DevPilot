namespace DevPilot.Contracts;

public sealed record EmbeddingBatchRequest(
    IReadOnlyList<string> Inputs,
    string? ModelId = null);
