namespace DevPilot.Contracts;

public sealed record RagRequest(
    string Question,
    string? RepositoryId,
    int MaxContextChunks);
