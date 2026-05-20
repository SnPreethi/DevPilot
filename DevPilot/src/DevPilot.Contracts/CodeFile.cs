namespace DevPilot.Contracts;

public sealed record CodeFile(
    FileMetadata Metadata,
    string Content);
