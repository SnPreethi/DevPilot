namespace DevPilot.Contracts;

public sealed record EmbeddingRequest(
    string Input,
    string? ModelId = null);
