namespace DevPilot.Contracts;

public sealed record FileMetadata(
    string Id,
    string RepositoryId,
    string RepositoryName,
    string AbsolutePath,
    string RelativePath,
    string Extension,
    string Language,
    long FileSize,
    string SHA256Hash,
    DateTimeOffset LastModifiedUtc);
