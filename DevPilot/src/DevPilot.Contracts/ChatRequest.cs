namespace DevPilot.Contracts;

public sealed record ChatRequest(
    string Prompt,
    IReadOnlyList<SearchResult> Context,
    string? ModelId = null);
