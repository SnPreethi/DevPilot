namespace DevPilot.Contracts;

public sealed record EmbeddingVector(
    string Id,
    string ChunkId,
    string ModelName,
    IReadOnlyList<float> Vector,
    int Dimensions,
    DateTimeOffset CreatedUtc,
    string EmbeddingModelVersion = "1",
    int EmbeddingSchemaVersion = 1,
    string ChunkHash = "",
    DateTimeOffset? IndexedAtUtc = null);
