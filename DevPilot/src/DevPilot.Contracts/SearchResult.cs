namespace DevPilot.Contracts;

public sealed record SearchResult(
    string ChunkId,
    string RepositoryId,
    string RelativePath,
    string Snippet,
    double Score);
