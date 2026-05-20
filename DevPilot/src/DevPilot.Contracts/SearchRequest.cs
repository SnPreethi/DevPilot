namespace DevPilot.Contracts;

public sealed record SearchRequest(
    string Query,
    int MaxResults,
    string? RepositoryId = null);
