namespace DevPilot.Contracts;

public sealed record RepositoryDescriptor(
    string Id,
    string Name,
    string RootPath,
    DateTimeOffset RegisteredAt);
