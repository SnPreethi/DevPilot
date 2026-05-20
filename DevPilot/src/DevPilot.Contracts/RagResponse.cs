namespace DevPilot.Contracts;

public sealed record RagResponse(
    string Answer,
    IReadOnlyList<SearchResult> Sources);
