namespace DevPilot.UI.Models;

public sealed record RepositoryItem(
    string Id,
    string Name,
    string RootPath,
    DateTimeOffset IndexedAtUtc,
    int FileCount,
    int ChunkCount);
