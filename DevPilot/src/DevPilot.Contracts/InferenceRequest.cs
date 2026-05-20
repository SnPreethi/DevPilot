using System.Collections.Generic;

namespace DevPilot.Contracts;

public sealed record InferenceRequest(
    string Prompt,
    string? ModelId,
    int MaxOutputTokens,
    double Temperature,
    double TopP,
    IReadOnlyList<RetrievedContext> Context,
    GenerationParameters? Parameters = null,
    bool RawPrompt = false);
