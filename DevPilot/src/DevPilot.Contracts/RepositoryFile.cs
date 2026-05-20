namespace DevPilot.Contracts;

public sealed record RepositoryFile(
    string RepositoryId,
    string RelativePath,
    string FullPath,
    long SizeInBytes,
    DateTimeOffset LastModifiedAt);
