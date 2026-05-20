namespace DevPilot.Contracts;

public sealed record InferenceResult(
    string Answer,
    string ModelId,
    TimeSpan Duration,
    int PromptTokenCount,
    int OutputTokenCount,
    bool UsedFallback);
