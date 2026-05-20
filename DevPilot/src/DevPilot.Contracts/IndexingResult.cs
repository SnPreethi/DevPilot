namespace DevPilot.Contracts;

public sealed record IndexingResult(
    string RepositoryId,
    string RepositoryName,
    int FilesScanned,
    int FilesIgnored,
    int ChunksCreated,
    int FilesSkipped = 0,
    int FilesDeleted = 0,
    int EmbeddingsCreated = 0,
    TimeSpan? Duration = null);
