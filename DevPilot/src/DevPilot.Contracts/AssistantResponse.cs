namespace DevPilot.Contracts;

public sealed record AssistantResponse(
    string Answer,
    IReadOnlyList<RetrievedContext> ReferencedContext,
    TimeSpan InferenceDuration,
    int PromptTokenCount,
    int OutputTokenCount,
    int RetrievedContextCount);
