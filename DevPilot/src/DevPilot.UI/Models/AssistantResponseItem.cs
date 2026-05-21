namespace DevPilot.UI.Models;

public sealed record AssistantResponseItem(
    string Answer,
    IReadOnlyList<string> ReferencedFiles,
    IReadOnlyList<SearchResultItem> RetrievedContext,
    TimeSpan InferenceDuration,
    int PromptTokens,
    int OutputTokens);
