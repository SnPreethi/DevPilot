namespace DevPilot.Contracts;

public sealed record RepositoryDocument(
    string RepositoryId,
    string RepositoryName,
    string RootPath,
    DateTimeOffset IndexedAtUtc);
