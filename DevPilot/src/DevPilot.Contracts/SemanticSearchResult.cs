namespace DevPilot.Contracts;

public sealed record SemanticSearchResult(
    string Query,
    IReadOnlyList<SearchMatch> Matches);
